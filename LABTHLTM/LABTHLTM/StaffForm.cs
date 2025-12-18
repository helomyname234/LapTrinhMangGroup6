using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LABTHLTM
{
    public partial class StaffForm : Form
    {
        private TcpClient tcpClient;
        private bool isConnect;

        private NetworkStream stream;
        private List<OrderView> orderList;


        public StaffForm()
        {
            InitializeComponent();
            orderList = new List<OrderView>();
        }

        private void StaffForm_Load(object sender, EventArgs e)
        {
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (!isConnect)
            {
                await ConnectToServer();
            }
            else
            {
                await DisconnectAsync();
            }
        }

        private async Task ConnectToServer()
        {
            if (string.IsNullOrWhiteSpace(txtServerIP.Text))
            {
                MessageBox.Show("Please type the IP server", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(txtPort.Text, out int port))
            {
                MessageBox.Show("Wrong port!!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnConnect.Enabled = false;

            try
            {
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(txtServerIP.Text, port);
                stream = tcpClient.GetStream();
                isConnect = true;

                
                await SendMessage("AUTH STAFF");
                string authResponse = await ReceiveMessage();

                btnConnect.Text = "Disconnect";
                btnConnect.BackColor = Color.FromArgb(231, 76, 60);


                MessageBox.Show($"Connected to {txtServerIP.Text}:{port} successfully!", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (SocketException sockEx)
            {

                string errorMsg = "Cannot connect to server!";
                MessageBox.Show(errorMsg, "Connection Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection error!\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            if (!isConnect)
            {
                MessageBox.Show("Please connect to server first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            await RefreshOrders();
        }

        private async Task RefreshOrders()
        {
            if (!isConnect)
                return;

            try
            {
                await SendMessage("GET_ORDERS");
                string orderData = await ReceiveMessage();
                LoadOrders(orderData);
            }
            catch
            {
                
            }
        }

        private void LoadOrders(string orderData)
        {
            if (dgvOrders.InvokeRequired)
            {
                dgvOrders.Invoke(new Action(() => LoadOrders(orderData)));
                return;
            }

            orderList.Clear();

            if (orderData == "EMPTY")
            {
                dgvOrders.DataSource = null;
                return;
            }

            string[] lines = orderData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string[] parts = line.Split(';');
                if (parts.Length == 6)
                {
                    orderList.Add(new OrderView
                    {
                        Table = int.Parse(parts[0]),
                        ItemID = int.Parse(parts[1]),
                        ItemName = parts[2],
                        Quantity = int.Parse(parts[3]),
                        Price = decimal.Parse(parts[4]),
                        Total = decimal.Parse(parts[5])
                    });
                }
            }

            dgvOrders.DataSource = null;
            dgvOrders.DataSource = orderList;

            if (dgvOrders.Columns.Count > 0)
            {
                dgvOrders.Columns["ItemID"].Visible = false;
                dgvOrders.Columns["Price"].DefaultCellStyle.Format = "N0";
                dgvOrders.Columns["Total"].DefaultCellStyle.Format = "N0";
            }
        }

        private async void btnCharge_Click(object sender, EventArgs e)
        {
            if (!isConnect)
            {
                MessageBox.Show("Please connect to server first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtTableNumber.Text))
            {
                MessageBox.Show("Please enter table number!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await ProcessPaymentAsync();
        }

        private async Task ProcessPaymentAsync()
        {
            btnCharge.Enabled = false;

            try
            {
                int tableNumber = int.Parse(txtTableNumber.Text);
                await SendMessage($"PAY {tableNumber}");
                string response = await ReceiveMessage();

                if (response.StartsWith("ERROR"))
                {
                    MessageBox.Show(response, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Parse bill details
                string[] lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                decimal total = 0;
                StringBuilder billText = new StringBuilder();

                billText.AppendLine("RESTAURANT BILL");
                billText.AppendLine($"Date: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                billText.AppendLine();

                foreach (string line in lines)
                {
                    if (line.StartsWith("TABLE"))
                    {
                        billText.AppendLine($"Table Number: {tableNumber}");
                        billText.AppendLine("--------------------------------");
                    }
                    else if (line.StartsWith("TOTAL"))
                    {
                        total = decimal.Parse(line.Split(' ')[1]);
                    }
                    else
                    {
                        string[] parts = line.Split(';');
                        if (parts.Length == 4)
                        {
                            string name = parts[0];
                            int qty = int.Parse(parts[1]);
                            decimal price = decimal.Parse(parts[2]);
                            decimal itemTotal = decimal.Parse(parts[3]);

                            billText.AppendLine($"{name}");
                            billText.AppendLine($"  {qty} x {price:N0} = {itemTotal:N0} VND");
                        }
                    }
                }

                billText.AppendLine("---------------------------------");
                billText.AppendLine($"TOTAL: {total:N0} VND");

                lblTotalAmount.Text = $"{total:N0} VND";
                lblTotalAmount.ForeColor = Color.Green;

                // Save to file
                string fileName = $"bill_Table{tableNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                await Task.Run(() => File.WriteAllText(fileName, billText.ToString()));

                MessageBox.Show($"Payment processed successfully!\n\nTotal: {total:N0} VND\n\nBill saved to: {fileName}",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                await RefreshOrders();
                txtTableNumber.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing payment: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnCharge.Enabled = true;
            }
        }

        private async Task SendMessage(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(data, 0, data.Length);
        }

        private async Task<string> ReceiveMessage()
        {
            byte[] buffer = new byte[4096];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }

        private async Task DisconnectAsync()
        {
            try
            {
                
                if (isConnect)
                {
                    await SendMessage("QUIT");
                    await Task.Delay(100);
                    stream?.Close();
                    tcpClient?.Close();
                }
            }
            catch { }
            finally
            {
                isConnect = false;
                btnConnect.Text = "Connect";
                btnConnect.BackColor = Color.FromArgb(46, 204, 113);
            }
        }

        private async void StaffForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            await DisconnectAsync();
        }

        private void txtTableNumber_TextChanged(object sender, EventArgs e)
        {

        }

        private void dgvOrders_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }
    }

    public class OrderView
    {
        public int Table { get; set; }
        public int ItemID { get; set; }
        public string ItemName { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }
    }
}
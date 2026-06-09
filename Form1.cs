using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.Configuration;

namespace WinFormsApp1
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            InitializeDataGridView();
            LoadData();

        }

        private void InitializeDataGridView()
        {
            DataGridViewButtonColumn clearDateButtonColumn = new DataGridViewButtonColumn();
            // Imposta alcune colonne come editabili (ad esempio la data di assegnazione e le note)
            clearDateButtonColumn.Name = "ClearDate";
            clearDateButtonColumn.HeaderText = "Azione";
            clearDateButtonColumn.Text = "Svuota Data";
            clearDateButtonColumn.UseColumnTextForButtonValue = true;
            dataGridViewPDL.Columns.Add(clearDateButtonColumn);
        }

        private void dataGridViewPDL_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // Evita errori su header o colonne non valide
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            if (dataGridViewPDL.Columns[e.ColumnIndex].Name == "ClearDate")
            {
                var row = dataGridViewPDL.Rows[e.RowIndex];

                // Ottieni l'ID della riga
                if (!int.TryParse(row.Cells[1].Value?.ToString(), out int idNes))
                {
                    MessageBox.Show("ID non valido.");
                    return; // Esci se l'ID non è valido
                }
                // Svuota la cella della data a video
                row.Cells[4].Value = DBNull.Value;

                // Esegui l'aggiornamento nel DB
                UpdatePianoLavoro(idNes, null, row.Cells[5].Value?.ToString());
            }
        }
        private void LoadData()
        {
            string connectionString = ConfigurationManager.ConnectionStrings["MyDatabase"].ConnectionString;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    string query = "SELECT        IDNES, NSDSC, MADSC, DTEXP, NSNOT, TEMPO, RIPET FROM            XV_A_NES_BOLLAIMPIANTO"; // Modifica questa query come necessario

                    SqlDataAdapter dataAdapter = new SqlDataAdapter(query, connection);
                    DataTable dataTable = new DataTable();
                    dataAdapter.Fill(dataTable);

                    dataGridViewPDL.DataSource = dataTable;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Errore nel caricamento dei piani di lavoro: " + ex.Message);
                }
            }
        }

        private void TxtBolla_TextChanged(object sender, EventArgs e)
        {

        }
        private void TxtImpianto_TextChanged(object sender, EventArgs e)
        {

        }

        private void TxtBolla_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {

                e.SuppressKeyPress = true; // Evita il "beep" quando viene premuto Invio

                // Imposta il focus sul campo Impianto
                txtImpianto.Focus();
            }
        }

        private void TxtImpianto_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // Evita il "beep" quando viene premuto Invio

                // Imposta il focus sul campo Impianto
                this.SubmitForm();
            }
        }

        private void SubmitForm()
        {
            {
                string bolla = txtBolla.Text;
                string impianto = txtImpianto.Text;

                string connectionString = ConfigurationManager.ConnectionStrings["MyDatabase"].ConnectionString;
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    Console.WriteLine("ora");
                    try
                    {
                        connection.Open();

                        // Chiamata alla stored procedure


                        using (SqlCommand command = new SqlCommand("dbo.[XSP_BOLLA_IMPIANTO]", connection))
                        {
                            command.CommandType = System.Data.CommandType.StoredProcedure;

                            // Aggiungi i parametri alla stored procedure
                            command.Parameters.AddWithValue("@bolla", bolla);
                            command.Parameters.AddWithValue("@impianto", impianto);

                            // Esegui la stored procedure
                            var result = command.ExecuteScalar();

                            // Aggiungi il risultato alla textbox txtLog
                            txtLog.Text += result.ToString() + Environment.NewLine;
                            txtLog.SelectionStart = txtLog.Text.Length;
                            txtLog.ScrollToCaret();
                            // Svuota i campi di input
                            txtBolla.Text = string.Empty;
                            txtImpianto.Text = string.Empty;

                            // Imposta il focus sul campo txtBolla
                            txtBolla.Focus();
                            LoadData();
                        }

                    }
                    catch (Exception ex)
                    {
                        txtLog.Text += "Si è verificato un errore durante l'esecuzione della stored procedure:";
                        txtLog.Text += ex.ToString();
                        txtLog.SelectionStart = txtLog.Text.Length;
                        txtLog.ScrollToCaret();
                    }
                    
                }


                //  OdbcConnectionStringBuilder connectionStringBuilder = new OdbcConnectionStringBuilder();
                //connectionStringBuilder.Dsn ="GABRIELLI";
                // connectionStringBuilder["Uid"] = "FSM00001"; // Sostituisci con il tuo username
                // connectionStringBuilder["Pwd"] = "CCDBUSER_04"; // Sostituisci con la tua password
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }


        private void DataGridViewPDL_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            // Controllo per assicurarsi che l'indice della riga sia valido
            if (e.RowIndex < 0 || e.RowIndex >= dataGridViewPDL.Rows.Count)
                return; // Esci se l'indice non è valido

            // Ottieni la riga corrente
            DataGridViewRow row = dataGridViewPDL.Rows[e.RowIndex];

            // Ottieni i valori modificati
            if (!int.TryParse(row.Cells[1].Value?.ToString(), out int idNes))
            {
                MessageBox.Show("ID non valido.");
                return; // Esci se l'ID non è valido
            }

            DateTime dataAssegnazione;
            string dataCellValue = row.Cells[4].Value?.ToString();
            if (dataCellValue == null || string.IsNullOrWhiteSpace(dataCellValue))
            {
                row.Cells[4].Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                dataCellValue  = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");// Imposta la data corrente se la cella è vuota
            }

            bool isValidDate = DateTime.TryParseExact(
               dataCellValue,
                "dd/MM/yyyy HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out dataAssegnazione);

            if (!isValidDate)
            {
                MessageBox.Show("Data di assegnazione non valida - ." + dataCellValue);
                return; // Esci se la data non è valida
            }

            string note = row.Cells[5].Value?.ToString();

            // Esegui l'aggiornamento nel database
            UpdatePianoLavoro(idNes, dataAssegnazione, note);
        }


        private void UpdatePianoLavoro(int idNes, DateTime? dataAssegnazione, string note)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["MyDatabase"].ConnectionString;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();

                    // Creazione della query di aggiornamento
                    string query = "UPDATE dbo.A_NES " +
                                   "SET DTEXP = @DataAssegnazione, NSNOT = @Note " +
                                   "WHERE IDNES = @IdNes";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@IdNes", idNes);
                        command.Parameters.AddWithValue("@DataAssegnazione", (object)dataAssegnazione ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Note", note ?? (object)DBNull.Value);

                        // Esegui l'aggiornamento
                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Errore durante l'aggiornamento del piano di lavoro: " + ex.Message);
                }
            }
        }

    }
}

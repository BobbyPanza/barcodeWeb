using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BollaImpianto
{
    public class DataGridViewCalendarColumn : DataGridViewColumn
    {
        public DataGridViewCalendarColumn() : base(new DataGridViewCalendarCell())
        {
        }

        public override DataGridViewCell CellTemplate
        {
            get
            {
                return base.CellTemplate;
            }
            set
            {
                // Assicurati che il valore assegnato sia di tipo DataGridViewCalendarCell
                if (value != null && !value.GetType().IsAssignableFrom(typeof(DataGridViewCalendarCell)))
                {
                    throw new InvalidCastException("Devi assegnare un oggetto del tipo DataGridViewCalendarCell.");
                }
                base.CellTemplate = value;
            }
        }
    }

    public class DataGridViewCalendarCell : DataGridViewTextBoxCell
    {
        public DataGridViewCalendarCell() : base()
        {
            // Usa DateTime come tipo di dati
            this.Style.Format = "dd/MM/yyyy"; // Imposta il formato della data senza ora
        }

        public override void InitializeEditingControl(int rowIndex, object initialFormattedValue, DataGridViewCellStyle dataGridViewCellStyle)
        {
            // Inizializza il controllo di modifica
            base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);
            DataGridViewCalendarEditingControl ctl = DataGridView.EditingControl as DataGridViewCalendarEditingControl;
            ctl.Value = this.Value == null || this.Value == DBNull.Value
                ? DateTime.Today
                : ((DateTime)this.Value).Date; // Imposta solo la data
        }

        public override Type EditType
        {
            // Usa il controllo DateTimePicker per l'editing
            get
            {
                return typeof(DataGridViewCalendarEditingControl);
            }
        }

        public override Type ValueType
        {
            // Imposta il tipo di dati come DateTime
            get
            {
                return typeof(DateTime);
            }
        }

        public override object DefaultNewRowValue
        {
            // Valore di default
            get
            {
                return DateTime.Today.Date; // Imposta solo la data
            }
        }
    }

    public class DataGridViewCalendarEditingControl : DateTimePicker, IDataGridViewEditingControl
    {
        DataGridView dataGridView;
        private bool valueChanged = false;

        public DataGridViewCalendarEditingControl()
        {
            // Usa il formato di data del sistema operativo
            this.Format = DateTimePickerFormat.Custom;
            this.CustomFormat = "dd/MM/yyyy"; // Rimuovi HH:mm:ss se non ti serve

            // Imposta il valore iniziale
            this.Value = DateTime.Today;// Imposta il valore iniziale
        }

        public object EditingControlFormattedValue
        {
            get
            {
                return this.Value.Date.ToString("dd/MM/yyyy"); // Restituisce solo la parte data
            }
            set
            {
                if (value is string)
                {
                    try
                    {
                        // Prova a convertire usando il formato italiano
                        this.Value = DateTime.ParseExact((string)value, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        // In caso di errore, imposta un valore di default
                        this.Value = DateTime.Today;
                    }
                }
            }
        }

        public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context)
        {
            return EditingControlFormattedValue;
        }

        public void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle)
        {
            this.Font = dataGridViewCellStyle.Font;
            this.CalendarForeColor = dataGridViewCellStyle.ForeColor;
            this.CalendarMonthBackground = dataGridViewCellStyle.BackColor;
        }

        public int EditingControlRowIndex { get; set; }

        public bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey)
        {
            // Lascia che DateTimePicker gestisca i tasti di navigazione
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Up:
                case Keys.Down:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                case Keys.PageDown:
                case Keys.PageUp:
                    return true;
                default:
                    return !dataGridViewWantsInputKey;
            }
        }

        public void PrepareEditingControlForEdit(bool selectAll)
        {
            // Non c'è bisogno di azioni specifiche qui
        }

        public bool RepositionEditingControlOnValueChange => false;

        public DataGridView EditingControlDataGridView
        {
            get { return dataGridView; }
            set { dataGridView = value; }
        }

        public bool EditingControlValueChanged
        {
            get { return valueChanged; }
            set { valueChanged = value; }
        }

        public Cursor EditingPanelCursor => base.Cursor;

        protected override void OnValueChanged(EventArgs eventargs)
        {
            if (this.EditingControlDataGridView != null) // Aggiungi verifica
            {
                // Quando il valore del DateTimePicker cambia, segnala che il valore è cambiato
                valueChanged = true;
                this.EditingControlDataGridView.NotifyCurrentCellDirty(true);
            }
            base.OnValueChanged(eventargs);
        }
    }
}

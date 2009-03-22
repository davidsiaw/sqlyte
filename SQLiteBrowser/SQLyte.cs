using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;

namespace SQLiteBrowser
{
    public partial class SQLyte : Form
    {
        public SQLyte(string[] args)
        {
            InitializeComponent();

            if (args.Length == 1)
            {
                OpenDatabase(args[0]);
            }
        }

        class Database : IDisposable
        {
            public class SQLiteMasterTableRow
            {
                public string type;
                public string name;
                public string tbl_name;
                public string rootpage;
                public string sql;
            }

            SQLite connection;
            public Database(string dbname)
            {
                connection = new SQLite();
                connection.OpenDatabase(dbname);
            }

            ~Database()
            {
                Dispose();
            }

            #region IDisposable Members

            bool disposed = false;
            public void Dispose()
            {
                if (!disposed)
                {
                    disposed = true;
                    connection.CloseDatabase();
                }
            }

            #endregion

            public SQLiteMasterTableRow[] GetDatabaseInformation()
            {
                List<SQLiteMasterTableRow> table = new List<SQLiteMasterTableRow>();
                DataTable tbl = connection.ExecuteQuery("SELECT * FROM sqlite_master");
                foreach (DataRow rw in tbl.Rows)
                {
                    SQLiteMasterTableRow row = new SQLiteMasterTableRow();
                    row.type = rw["type"].ToString();
                    row.name = rw["name"].ToString();
                    row.tbl_name = rw["tbl_name"].ToString();
                    row.rootpage = rw["rootpage"].ToString();
                    row.sql = rw["sql"].ToString();
                    table.Add(row);
                }

                return table.ToArray();
            }

            public void GetDatabaseInformation(DataGridView datagrid)
            {
                PopulateDataGridWithQuery(datagrid, "SELECT * FROM sqlite_master");
            }

            public double PopulateDataGridWithQuery(DataGridView datagrid, string query)
            {
                DateTime start = DateTime.Now;
                DataTable tbl = connection.ExecuteQuery(query);
                datagrid.Rows.Clear();
                datagrid.Columns.Clear();

                foreach (DataColumn column in tbl.Columns)
                {
                    datagrid.Columns.Add(column.ColumnName, column.ColumnName);
                }

                foreach (DataRow row in tbl.Rows)
                {
                    int index = datagrid.Rows.Add();
                    for (int i = 0; i < row.ItemArray.Length; i++)
                    {
                        datagrid.Rows[index].Cells[i].Value = row.ItemArray[i];
                    }
                }
                TimeSpan t = DateTime.Now - start;
                return t.TotalMilliseconds;
            }

            bool querying = false;
            ManualResetEvent querythreadready = new ManualResetEvent(true);

            public void PopulateDataGridWithQueryAsync(DataGridView datagrid, string query, Action<int> foreachrow, Action<double> complete)
            {
                // If we are in the middle of a query, cancel it, then wait until the querying has stopped so we can retry this function
                if (querying)
                {   
                    querying = false;
                    Thread waiter = new Thread(new ThreadStart(() =>
                    {
                        querythreadready.Reset();
                        querythreadready.WaitOne(1000);
                        datagrid.Invoke(new Action(() => PopulateDataGridWithQueryAsync(datagrid, query, foreachrow, complete)));
                    }));
                    waiter.Start();
                    return;
                }

                datagrid.Rows.Clear();
                datagrid.Columns.Clear();

                int rowcount = 0;
                DateTime start = DateTime.Now;
                querying = true;
                connection.AsyncExecuteQuery(query,
                row =>
                    {
                        datagrid.Invoke(new Action(() =>
                        {
                            if (rowcount == 0)
                            {
                                foreach (DataColumn column in row.Table.Columns)
                                {
                                    datagrid.Columns.Add(column.ColumnName, column.ColumnName);
                                }
                            }

                            int index = datagrid.Rows.Add();
                            for (int i = 0; i < row.ItemArray.Length; i++)
                            {
                                datagrid.Rows[index].Cells[i].Value = row.ItemArray[i];
                            }

                            foreachrow(rowcount);
                            rowcount++;
                        }));

                        if (!querying)
                        {
                            querythreadready.Set();
                            return false;
                        }
                        return true;
                    }
                , () =>
                    {
                        TimeSpan t = DateTime.Now - start;
                        complete(t.TotalMilliseconds);
                        querying = false;
                    });
            }

        }

        Database db = null;


        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (db != null)
            {
                db.Dispose();
                data_schema.Rows.Clear();
            }

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.ShowDialog();
            if (!string.IsNullOrEmpty(ofd.FileName))
            {
                OpenDatabase(ofd.FileName);
            }
        }

        private void OpenDatabase(string file)
        {
            try
            {
                db = new Database(file);
                db.GetDatabaseInformation(data_schema);

                Database.SQLiteMasterTableRow[] schemas = db.GetDatabaseInformation();
                foreach (Database.SQLiteMasterTableRow row in schemas)
                {
                    if (row.type.ToUpper() == "TABLE")
                    {
                        list_tables.Items.Add(row.name);
                    }
                }

                toolStripStatusLabel1.Text = "Database loaded successfully";
            }
            catch (Exception exp)
            {
                MessageBox.Show(exp.Message);
            }
        }

        private void list_tables_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (list_tables.SelectedItem != null)
            {
                try
                {
                    db.PopulateDataGridWithQueryAsync(data_browsetable, "SELECT * FROM " + list_tables.SelectedItem.ToString(),
                        rowcount => this.toolStripStatusLabel1.Text = "Loaded rows: " + rowcount.ToString(),
                        milliseconds => this.toolStripStatusLabel1.Text = "Query complete in " + milliseconds + " ms");
                }
                catch (Exception exp)
                {
                    MessageBox.Show(exp.Message);
                }
            }
        }

        private void btn_execute_Click(object sender, EventArgs e)
        {
            try
            {
                db.PopulateDataGridWithQueryAsync(data_queryresults, txt_query.Text,
                    rowcount => this.toolStripStatusLabel1.Text = "Loaded rows: " + rowcount.ToString(),
                    milliseconds => this.toolStripStatusLabel1.Text = "Query complete in " + milliseconds + " ms");
            }
            catch (Exception exp)
            {
                MessageBox.Show(exp.Message);
            }
        }

        private void btn_explain_Click(object sender, EventArgs e)
        {
            try
            {
                double milliseconds = db.PopulateDataGridWithQuery(data_queryresults, "EXPLAIN " + txt_query.Text);
                this.toolStripStatusLabel1.Text = "Query complete in " + milliseconds + " ms";
            }
            catch (Exception exp)
            {
                MessageBox.Show(exp.Message);
            }
        }
    }
}

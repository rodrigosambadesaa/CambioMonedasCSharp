using System.Globalization;
using System.Numerics;
using ProgVorazCs.Core;

namespace ProgVorazCs.Gui;

public partial class Form1 : Form
{
    private readonly CurrencyRepository repo;

    private ComboBox cmbCurrency = null!;
    private ComboBox cmbDenomination = null!;
    private ListBox lstStock = null!;
    private ListBox lstResult = null!;
    private TextBox txtDelta = null!;
    private TextBox txtAmount = null!;
    private RadioButton rbLimited = null!;
    private RadioButton rbUnlimited = null!;
    private Button btnLoad = null!;
    private Button btnReload = null!;
    private Button btnAdd = null!;
    private Button btnRemove = null!;
    private Button btnCalculate = null!;

    private CurrencyData? current;
    private string currentKey = string.Empty;

    public Form1()
    {
        InitializeComponent();

        string? dataRoot = FindDataRoot();
        if (dataRoot is null)
        {
            MessageBox.Show("No se encontraron monedas.txt y stock.txt.", "Inicio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            dataRoot = Environment.CurrentDirectory;
        }

        repo = new CurrencyRepository(dataRoot);
        BuildUi();
        ReloadCurrencies();
    }

    private void BuildUi()
    {
        Text = "ProgVoraz C# - Interfaz Grafica";
        ClientSize = new Size(800, 620);

        Label lblCurrency = new() { Left = 20, Top = 20, Width = 80, Text = "Moneda:" };
        cmbCurrency = new ComboBox { Left = 100, Top = 16, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        btnLoad = new Button { Left = 290, Top = 15, Width = 90, Text = "Cargar" };
        btnReload = new Button { Left = 390, Top = 15, Width = 90, Text = "Recargar" };

        rbLimited = new RadioButton { Left = 20, Top = 50, Width = 120, Text = "Stock limitado", Checked = true };
        rbUnlimited = new RadioButton { Left = 150, Top = 50, Width = 120, Text = "Stock ilimitado" };

        lstStock = new ListBox { Left = 20, Top = 80, Width = 760, Height = 200, HorizontalScrollbar = true };

        Label lblAdmin = new() { Left = 20, Top = 300, Width = 160, Text = "Panel Administrador" };
        cmbDenomination = new ComboBox { Left = 20, Top = 330, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        txtDelta = new TextBox { Left = 210, Top = 330, Width = 180 };
        btnAdd = new Button { Left = 400, Top = 328, Width = 90, Text = "Anadir" };
        btnRemove = new Button { Left = 500, Top = 328, Width = 90, Text = "Quitar" };

        Label lblReturn = new() { Left = 20, Top = 380, Width = 160, Text = "Panel Devolucion" };
        txtAmount = new TextBox { Left = 20, Top = 410, Width = 240 };
        btnCalculate = new Button { Left = 270, Top = 408, Width = 150, Text = "Calcular devolucion" };

        lstResult = new ListBox { Left = 20, Top = 450, Width = 760, Height = 150, HorizontalScrollbar = true };

        Controls.AddRange([
            lblCurrency, cmbCurrency, btnLoad, btnReload, rbLimited, rbUnlimited,
            lstStock, lblAdmin, cmbDenomination, txtDelta, btnAdd, btnRemove,
            lblReturn, txtAmount, btnCalculate, lstResult
        ]);

        btnLoad.Click += (_, _) => LoadSelectedCurrency();
        btnReload.Click += (_, _) => ReloadCurrencies();
        btnAdd.Click += (_, _) => ApplyStockChange(add: true);
        btnRemove.Click += (_, _) => ApplyStockChange(add: false);
        btnCalculate.Click += (_, _) => CalculateChange();
        rbLimited.CheckedChanged += (_, _) => { lstResult.Items.Clear(); RefreshStockView(); };
        rbUnlimited.CheckedChanged += (_, _) => { lstResult.Items.Clear(); RefreshStockView(); };
    }

    private bool IsLimitedMode => rbLimited.Checked;

    private void ReloadCurrencies()
    {
        cmbCurrency.Items.Clear();
        foreach (string name in repo.LoadCurrencyNames())
        {
            cmbCurrency.Items.Add(name);
        }

        if (cmbCurrency.Items.Count > 0)
        {
            cmbCurrency.SelectedIndex = 0;
            LoadSelectedCurrency();
        }
    }

    private void LoadSelectedCurrency()
    {
        if (cmbCurrency.SelectedItem is not string selected)
        {
            return;
        }

        currentKey = TextKey.Normalize(selected);
        current = repo.LoadCurrency(currentKey);
        if (current is null)
        {
            MessageBox.Show("No se pudo cargar moneda/stock.", "Carga", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        lstResult.Items.Clear();
        RefreshStockView();
    }

    private void RefreshStockView()
    {
        lstStock.Items.Clear();
        cmbDenomination.Items.Clear();

        if (current is null)
        {
            return;
        }

        for (int i = 0; i < current.Denominations.Count; i++)
        {
            string line = IsLimitedMode
                ? $"Denom {current.Denominations[i]} c -> stock {current.Stock[i]}"
                : $"Denom {current.Denominations[i]} c -> stock ilimitado";

            lstStock.Items.Add(line);
            cmbDenomination.Items.Add(current.Denominations[i].ToString(CultureInfo.InvariantCulture));
        }

        if (cmbDenomination.Items.Count > 0)
        {
            cmbDenomination.SelectedIndex = 0;
        }

        cmbDenomination.Enabled = IsLimitedMode;
        txtDelta.Enabled = IsLimitedMode;
        btnAdd.Enabled = IsLimitedMode;
        btnRemove.Enabled = IsLimitedMode;
    }

    private void ApplyStockChange(bool add)
    {
        if (current is null)
        {
            return;
        }

        if (!IsLimitedMode)
        {
            MessageBox.Show("El panel administrador aplica solo a stock limitado.", "Administrador", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (cmbDenomination.SelectedIndex < 0)
        {
            return;
        }

        if (!BigInteger.TryParse(txtDelta.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger delta) || delta < 0)
        {
            MessageBox.Show("Cantidad invalida.", "Administrador", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        int idx = cmbDenomination.SelectedIndex;
        // El valor mostrado puede ser antiguo. Comprobar y modificar la
        // versión actual del inventario dentro de una sola transacción.
        if (!repo.TryAdjustStock(currentKey, idx, delta, add,
            out List<BigInteger> persistedStock))
        {
            current.Stock = repo.LoadStock(currentKey) ?? current.Stock;
            RefreshStockView();
            MessageBox.Show("Inventario modificado por otro proceso, insuficiente o no guardado. Revise el stock y reintente.",
                "Administrador", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        current.Stock = persistedStock;
        txtDelta.Clear();
        RefreshStockView();
    }

    private void CalculateChange()
    {
        if (current is null)
        {
            return;
        }

        if (!BigInteger.TryParse(txtAmount.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger amount) || amount < 0)
        {
            MessageBox.Show("Monto invalido.", "Cambio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        bool ok;
        List<BigInteger> solution;
        lstResult.Items.Clear();
        lstResult.Items.Add($"Cambio solicitado: {amount} c");

        if (IsLimitedMode)
        {
            ok = ChangeCalculator.ComputeLimited(amount, current.Denominations, current.Stock,
                out solution, out _);
            if (ok)
            {
                // No mostrar una devolución como realizada si la persistencia
                // falla o si otra instancia ha agotado el inventario.
                if (!repo.TryConsumeStock(currentKey, solution, out List<BigInteger> persistedStock))
                {
                    current.Stock = repo.LoadStock(currentKey) ?? current.Stock;
                    lstResult.Items.Add("Operación rechazada: stock modificado o error al guardar.");
                    RefreshStockView();
                    MessageBox.Show("No se ha descontado ninguna moneda. Recargue el inventario y reintente.",
                        "Cambio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                current.Stock = persistedStock;
            }
        }
        else
        {
            ok = ChangeCalculator.ComputeUnlimited(amount, current.Denominations, out solution);
        }

        if (!ok)
        {
            lstResult.Items.Add("No existe devolucion exacta para ese monto.");
            return;
        }

        for (int i = 0; i < current.Denominations.Count; i++)
        {
            if (solution[i].IsZero)
            {
                continue;
            }

            lstResult.Items.Add($"{current.Denominations[i]} c -> {solution[i]}");
        }

        txtAmount.Clear();
        RefreshStockView();
    }

    private static string? FindDataRoot()
    {
        DirectoryInfo? dir = new(Environment.CurrentDirectory);
        while (dir is not null)
        {
            string monedas = Path.Combine(dir.FullName, "monedas.txt");
            string stock = Path.Combine(dir.FullName, "stock.txt");
            if (File.Exists(monedas) && File.Exists(stock))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}

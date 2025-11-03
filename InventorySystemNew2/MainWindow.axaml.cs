using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace InventorySystemNew2;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent(); // virker, når x:Class i XAML matcher navnerummet
        DataContext = this;

        // --- Opret kunder og ordrer ---
        var sara = new Customer("Sara");
        var o1 = new Order();
        o1.OrderLines.Add(new OrderLine(ServoMotor, 1)); // 1 servo motor
        o1.OrderLines.Add(new OrderLine(PlcModule, 2));  // 2 PLC modules
        sara.CreateOrder(OrderBook, o1);

        var carl = new Customer("Carl");
        var o2 = new Order();
        o2.OrderLines.Add(new OrderLine(HydraulicPumpOil, 15)); // 15 liters of oil
        carl.CreateOrder(OrderBook, o2);
    }

    // --- Varer i lageret ---
    private readonly Item HydraulicPumpOil = new BulkItem
    {
        Name = "hydraulic pump oil",
        PricePerUnit = 59m,
        MeasurementUnit = "L"
    };

    private readonly Item PlcModule = new UnitItem
    {
        Name = "PLC module",
        PricePerUnit = 1250m,
        Weight = 1m,
        InventoryLocation = 1
    };

    private readonly Item ServoMotor = new UnitItem
    {
        Name = "servo motor",
        PricePerUnit = 2100m,
        Weight = 2m,
        InventoryLocation = 2
    };

    public OrderBook OrderBook { get; } = new();

    // --- Knappen i GUI ---
    public async void ProcessNextOrder_OnClick(object? sender, RoutedEventArgs e)
    {
        StatusMessages.Text += "Processing next order..." + Environment.NewLine;

        var orderLines = OrderBook.ProcessNextOrder();

        if (orderLines == null || orderLines.Count == 0)
        {
            StatusMessages.Text += "No orders in queue." + Environment.NewLine;
            return;
        }

        var robot = new ItemSorterRobot();

        foreach (var line in orderLines)
        {
            for (int i = 0; i < line.Quantity; i++)
            {
                StatusMessages.Text += $"Picking up {line.Item.Name} (Box {line.Item.InventoryLocation})" + Environment.NewLine;
                robot.PickUp(line.Item.InventoryLocation);
                await Task.Delay(9500); // venter 9,5 sek for bevægelse
            }
        }

        StatusMessages.Text += "Order completed. Shipment box ready." + Environment.NewLine;
    }
}

// ------------------ Robotklasse ------------------
public class ItemSorterRobot
{
    public string IpAddress = "localhost";
    public const int urscriptPort = 30002, dashboardPort = 29999;

    private void SendString(int port, string message)
    {
        using var client = new TcpClient(IpAddress, port);
        using var stream = client.GetStream();
        stream.Write(Encoding.ASCII.GetBytes(message));
    }

    private void SendUrscript(string urscript)
    {
        SendString(dashboardPort, "brake release\n");
        SendString(urscriptPort, urscript);
    }

    // URScript-template med nye koordinater (for robotten)
    public const string UrscriptTemplate = @"
def move_item_to_shipment_box():
    SBOX_X = 3
    SBOX_Y = 3
    ITEM_X = {0}
    ITEM_Y = 1
    DOWN_Z = 1

    def moveto(x, y, z = 0):
        SEP = 0.1
        p_target = p[x*SEP, -0.45 + y*SEP, 0.25 + z*SEP, d2r(180), 0, 0]
        movej(get_inverse_kin(p_target), a=1.2, v=0.25)
    end

    moveto(ITEM_X, ITEM_Y)
    moveto(ITEM_X, ITEM_Y, -DOWN_Z)
    moveto(ITEM_X, ITEM_Y)
    moveto(SBOX_X, SBOX_Y)
    moveto(SBOX_X, SBOX_Y, -DOWN_Z)
    moveto(SBOX_X, SBOX_Y)
end
";

    public void PickUp(uint itemId)
    {
        var script = string.Format(UrscriptTemplate, itemId);
        SendUrscript(script);
    }
}

// ------------------ Dataklasser ------------------
public class Item
{
    public string Name { get; set; } = "";
    public decimal PricePerUnit { get; set; }
    public uint InventoryLocation { get; set; }
}

public class BulkItem : Item
{
    public string MeasurementUnit { get; set; } = "";
}

public class UnitItem : Item
{
    public decimal Weight { get; set; }
}

public class OrderLine
{
    public Item Item { get; set; }
    public int Quantity { get; set; }

    public OrderLine(Item item, int quantity)
    {
        Item = item;
        Quantity = quantity;
    }
}

public class Order
{
    public List<OrderLine> OrderLines { get; set; } = new();
    public DateTime Time { get; set; } = DateTime.Now;
}

public class Customer
{
    public string Name { get; }
    public List<Order> Orders { get; } = new();

    public Customer(string name)
    {
        Name = name;
    }

    public void CreateOrder(OrderBook book, Order order)
    {
        Orders.Add(order);
        book.QueuedOrders.Add(order);
    }
}

public class OrderBook
{
    public List<Order> QueuedOrders { get; } = new();
    public List<Order> ProcessedOrders { get; } = new();

    // Returnerer ordrelinjerne i næste ordre
    public List<OrderLine> ProcessNextOrder()
    {
        var result = new List<OrderLine>();
        if (QueuedOrders.Count == 0) return result;

        var next = QueuedOrders[0];
        QueuedOrders.RemoveAt(0);
        ProcessedOrders.Add(next);

        result.AddRange(next.OrderLines);
        return result;
    }
}

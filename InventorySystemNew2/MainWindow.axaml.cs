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
        // Dette kalder den automatiske opsætning af vinduet fra XAML-filen
        InitializeComponent();

        // Gør det muligt for GUI-elementer (som knapper og tekstfelter)
        // at få adgang til data og metoder i denne klasse
        DataContext = this;

        // Her opretter vi nogle kunder og deres ordrer.
        // Det gør vi for at have noget at teste robotten med.
        var sara = new Customer("Sara");
        var o1 = new Order();
        // Sara bestiller en servo motor og to PLC-moduler
        o1.OrderLines.Add(new OrderLine(ServoMotor, 1));
        o1.OrderLines.Add(new OrderLine(PlcModule, 2));
        // Ordren bliver registreret i systemet
        sara.CreateOrder(OrderBook, o1);

        // Nu laver vi en ny kunde, Carl, som også laver en ordre
        var carl = new Customer("Carl");
        var o2 = new Order();
        // Carl bestiller 15 liter hydraulikolie
        o2.OrderLines.Add(new OrderLine(HydraulicPumpOil, 15));
        carl.CreateOrder(OrderBook, o2);
    }

    // Her oprettes nogle varer, som findes i lageret.
    // Nogle varer kan robotten hente (fordi de har en boks), andre kan ikke.
    private readonly Item HydraulicPumpOil = new BulkItem
    {
        Name = "hydraulic pump oil", // Navn på varen
        PricePerUnit = 59m,          // Pris per liter
        MeasurementUnit = "L"        // Måleenhed
        // Ingen InventoryLocation, fordi robotten ikke henter flydende varer
    };

    private readonly Item PlcModule = new UnitItem
    {
        Name = "PLC module",    // Navn på varen
        PricePerUnit = 1250m,   // Pris per stk.
        Weight = 1m,            // Vægt i kg
        InventoryLocation = 1   // Boksen på lageret, hvor varen ligger
    };

    private readonly Item ServoMotor = new UnitItem
    {
        Name = "servo motor",   // Navn på varen
        PricePerUnit = 2100m,   // Pris per stk.
        Weight = 2m,            // Vægt i kg
        InventoryLocation = 2   // Denne ligger i boks nummer 2
    };

    // OrderBook holder styr på alle ordrer – både de ventende og dem, der er færdige
    public OrderBook OrderBook { get; } = new();

    // Denne metode kører, når man trykker på knappen "Process next order" i GUI’en.
    // Den finder den næste ordre og får robotten til at hente varerne i den rigtige rækkefølge.
    public async void ProcessNextOrder_OnClick(object? sender, RoutedEventArgs e)
    {
        // Tilføjer en statusbesked i vinduet, så vi kan følge med
        StatusMessages.Text += "Processing next order..." + Environment.NewLine;

        // Her henter vi næste ordre i køen
        var orderLines = OrderBook.ProcessNextOrder();

        // Hvis der ikke er flere ordrer, vises en besked til brugeren
        if (orderLines == null || orderLines.Count == 0)
        {
            StatusMessages.Text += "No orders in queue." + Environment.NewLine;
            return;
        }

        // Vi opretter et nyt robotobjekt, som vi kan give kommandoer til
        var robot = new ItemSorterRobot();

        // Vi går gennem hver ordrelinje i ordren
        foreach (var line in orderLines)
        {
            // Nogle varer (som olie) kan ikke hentes af robotten, så vi springer dem over
            if (line.Item.InventoryLocation == 0)
            {
                StatusMessages.Text += $"Skipping '{line.Item.Name}' (no inventory location / bulk item)" + Environment.NewLine;
                continue;
            }

            // For hver varelinje henter robotten det antal varer, som står i Quantity
            for (int i = 0; i < line.Quantity; i++)
            {
                // Skriver i GUI’en, hvad der bliver hentet lige nu
                StatusMessages.Text +=
                    $"Picking up {line.Item.Name} (Box {line.Item.InventoryLocation})" + Environment.NewLine;

                // Her kaldes metoden, som sender et URScript-program til robotten
                robot.PickUp(line.Item.InventoryLocation);

                // Robotten bruger ca. 9,5 sekunder på at hente og flytte varen
                // Task.Delay bruges i stedet for Thread.Sleep, så GUI’en ikke fryser
                await Task.Delay(9500);
            }
        }

        // Når alle varer i ordren er hentet, skriver vi, at ordren er færdig
        StatusMessages.Text += "Order completed. Shipment box ready." + Environment.NewLine;
    }
}

// Klassen Robot står for kommunikationen mellem programmet og robotarmen.
// Den kan sende beskeder via netværk, som robotten kan forstå.
public class Robot
{
    // Standardporte, som bruges af robotten
    public const int urscriptPort = 30002, dashboardPort = 29999;
    public string IpAddress = "localhost"; // Her bruges lokal adresse til simulatoren

    // Denne metode sender almindelig tekst til robotten over TCP (netværksforbindelse)
    public void SendString(int port, string message)
    {
        // Opretter en forbindelse til robotten
        using var client = new TcpClient(IpAddress, port);

        // Åbner en stream, så vi kan sende data
        using var stream = client.GetStream();

        // Gør teksten klar til at blive sendt som bytes
        var data = Encoding.ASCII.GetBytes(message);

        // Skriver beskeden ud på netværket
        stream.Write(data, 0, data.Length);
    }

    // Denne metode bruges til at sende et helt URScript-program til robotten
    public void SendUrscript(string urscript)
    {
        // Først frigøres bremsen på robotarmen, så den kan bevæge sig
        SendString(dashboardPort, "brake release\n");

        // Derefter sendes selve scriptet, som robotten skal udføre
        SendString(urscriptPort, urscript);
    }
}

// Denne klasse bygger oven på Robot-klassen.
// Her fortæller vi præcist, hvordan robotten skal bevæge sig for at hente varer.
public class ItemSorterRobot : Robot
{
    // Dette er en tekstskabelon for robotprogrammets bevægelser.
    // {0} bliver udskiftet med koordinatet (X-aksen) for den boks, hvor varen ligger.
    public const string UrscriptTemplate = @"
def move_item_to_shipment_box():
    SBOX_X = 3
    SBOX_Y = 3
    ITEM_X = {0}
    ITEM_Y = 1
    DOWN_Z = 1

    # Denne funktion flytter robotarmen til de ønskede koordinater (x, y, z)
    def moveto(x, y, z = 0):
        SEP = 0.1
        # Robotten bruger 'p_target' som sit målpunkt i 3D-rummet
        p_target = p[x*SEP, -0.45 + y*SEP, 0.25 + z*SEP, d2r(180), 0, 0]
        # movej flytter armen jævnt fra punkt til punkt
        movej(get_inverse_kin(p_target), a=1.2, v=0.25)
    end

    # Flyt over den boks, hvor varen ligger
    moveto(ITEM_X, ITEM_Y)
    # Kør ned for at samle varen op
    moveto(ITEM_X, ITEM_Y, -DOWN_Z)
    # Gå op igen med varen
    moveto(ITEM_X, ITEM_Y)
    # Kør over til forsendelsesboksen (S)
    moveto(SBOX_X, SBOX_Y)
    # Kør ned for at aflevere varen
    moveto(SBOX_X, SBOX_Y, -DOWN_Z)
    # Gå op igen – klar til næste vare
    moveto(SBOX_X, SBOX_Y)
end
";

    // Denne metode gør det nemt at bruge robotten:
    // Vi giver den bare et nummer på boksen (1, 2 eller 3),
    // og den udskifter {0} i skabelonen med det tal.
    public void PickUp(uint inventoryLocation)
    {
        // Her erstattes {0} i teksten med det rigtige koordinat (inventoryLocation)
        var urscript = string.Format(UrscriptTemplate, inventoryLocation);

        // Robotten får derefter scriptet sendt, så den ved, hvad den skal gøre
        SendUrscript(urscript);
    }
}

// En vare i systemet. Indeholder navn, pris og placering i lageret.
public class Item
{
    public string Name { get; set; } = "";       // Navnet på varen
    public decimal PricePerUnit { get; set; }    // Prisen pr. stk. eller pr. enhed
    public uint InventoryLocation { get; set; }  // Hvilken boks varen ligger i
}

// Bruges til varer, som måles i liter, kilo osv. (ikke i antal)
public class BulkItem : Item
{
    public string MeasurementUnit { get; set; } = ""; // F.eks. "L" for liter
}

// Bruges til varer, som man tæller én for én
public class UnitItem : Item
{
    public decimal Weight { get; set; } // Vægten af én enhed
}

// En ordrelinje beskriver én type vare i en ordre og hvor mange af den type der bestilles
public class OrderLine
{
    public Item Item { get; set; }   // Referencen til varen
    public int Quantity { get; set; } // Antallet der bestilles

    // Når man laver en ny ordrelinje, skal man fortælle hvilken vare og hvor mange
    public OrderLine(Item item, int quantity)
    {
        Item = item;
        Quantity = quantity;
    }
}

// En ordre består af flere ordrelinjer.
// Den har også et tidspunkt, som viser, hvornår den blev lavet.
public class Order
{
    public List<OrderLine> OrderLines { get; set; } = new(); // Listen med alle varelinjerne
    public DateTime Time { get; set; } = DateTime.Now;       // Hvornår ordren blev oprettet
}

// En kunde kan have flere ordrer.
// Kunden har et navn og kan selv oprette nye ordrer.
public class Customer
{
    public string Name { get; }              // Kundens navn
    public List<Order> Orders { get; } = new(); // Liste over alle kundens ordrer

    public Customer(string name)
    {
        Name = name;
    }

    // Denne metode bruges, når kunden laver en ny ordre.
    // Ordren bliver gemt hos kunden og lagt i ordresystemets kø.
    public void CreateOrder(OrderBook book, Order order)
    {
        Orders.Add(order);          // Gemmer ordren hos kunden
        book.QueuedOrders.Add(order); // Lægger ordren i køen til behandling
    }
}

// OrderBook fungerer som en "ordreliste".
// Den holder styr på ordrer, der venter, og dem, der er blevet behandlet.
public class OrderBook
{
    public List<Order> QueuedOrders { get; } = new();   // Ordrer der venter på at blive behandlet
    public List<Order> ProcessedOrders { get; } = new(); // Ordrer der er færdige

    // Denne metode finder den næste ordre i køen og returnerer dens varer (ordrelinjer)
    public List<OrderLine> ProcessNextOrder()
    {
        // Hvis der ingen ordrer er, returneres en tom liste
        if (QueuedOrders.Count == 0)
            return new List<OrderLine>();

        // Hent den første ordre i køen (den ældste)
        var next = QueuedOrders[0];

        // Fjern den fra køen, da den nu skal behandles
        QueuedOrders.RemoveAt(0);

        // Flyt den over i listen over behandlede ordrer
        ProcessedOrders.Add(next);

        // Returnér ordrelinjerne, så robotten ved, hvad den skal hente
        return new List<OrderLine>(next.OrderLines);
    }
}


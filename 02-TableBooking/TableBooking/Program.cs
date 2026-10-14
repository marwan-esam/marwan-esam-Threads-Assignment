using TableBooking;

var booking = new FridayNightBooking();
Task[] customers = new Task[40];

for (int i = 0; i < customers.Length; i++)
{
    string name = $"Customer-{i + 1:00}";
    customers[i] = booking.BookAsync(name);
}

foreach (var t in customers)
    await t;

Console.WriteLine();
Console.WriteLine($"Booked={booking.Booked}, TablesLeft={booking.TablesLeft}");

using DJ.Codes;

ApexQueue<string> queue = new();

queue.Add("send daily report",     priority: 1);
queue.Add("restart failing service", priority: 10);
queue.Add("rotate logs",           priority: 3);
queue.Add("deploy hotfix",         priority: 10);
queue.Add("cleanup temp files",    priority: 2);

Console.WriteLine($"Items queued : {queue.Count()}");
Console.WriteLine($"Max priority : {queue.MaxPriority}");
Console.WriteLine();

while (queue.Count() > 0)
{
    Console.WriteLine($"  [priority {queue.MaxPriority,2}] {queue.Take()}");
}

Console.WriteLine();
Console.WriteLine($"After drain — Count: {queue.Count()}, MaxPriority: {queue.MaxPriority}");

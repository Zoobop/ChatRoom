using ClassChatAPI;
using ClassChatAPI.Core;

while (true)
{
    Console.Write("Enter your username: ");
    var username = Console.ReadLine();

    if (!string.IsNullOrEmpty(username))
    {
        var client = new ClassChatClient(ClassChatApplication.DefaultPort);
        client.Register(username);
        break;
    }
}
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClassChatAPI.Users;
using ClassChatAPI.Utility;

namespace ClassChatAPI.Core;

public sealed class ClassChatServer
{
    private readonly ClassChatApplication _classChatApplication;
    private readonly TcpListener _listener;

    public IPAddress IpAddress { get; }
    public int Port { get; }
    public IDictionary<TcpClient, User> Clients { get; } = new ConcurrentDictionary<TcpClient, User>();

    private volatile bool _isOnline = false;

    public ClassChatServer(ClassChatApplication classChat, int port)
    {
        _classChatApplication = classChat;

        var hostIp = Dns.GetHostEntry(Dns.GetHostName());
        IpAddress = hostIp.AddressList[2];
        Port = port;
        
        var localEndPoint = new IPEndPoint(IpAddress, Port);
        _listener = new TcpListener(localEndPoint);
    }

    public void Run()
    {
        Console.Title = "Server";
        
        // Set online status
        _isOnline = true;
        Console.Clear();
        Console.WriteLine("Initializing Server...");

        ThreadPool.SetMaxThreads(8, 8);
        ThreadPool.QueueUserWorkItem(GetConnections);

        // Construct a thread to manage client connection requests
        var connectionManagerWorker = new Thread(ManageConnections);
        var applicationWorker = new Thread(Application);
        connectionManagerWorker.Start();
        applicationWorker.Start();
    }
    
    private void GetConnections(object? arg)
    {
        _listener.Start();
        
        try
        {
            Console.WriteLine($"Running ClassChat Server on [{IpAddress}:{Port}]");
            Console.WriteLine("Accepting Client Connections...");
            
            while (_isOnline)
            {
                // Register clients
                TryRegisterClient(_listener);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
        
        _listener.Stop();
    }

    private void ManageConnections()
    {
        while (_isOnline)
        {
            // Reduce manage interval
            Thread.Sleep(1000);

            // Refresh active clients
            foreach (var (client, user) in Clients)
            {
                // Remove if cannot ping client
                if (!IsClientActive(client))
                {
                    client.Dispose();
                    client.Close();
                    
                    Clients.Remove(client);
                    _classChatApplication.UserRegistry.Remove(client);

                    Console.WriteLine($"Client Disconnected: {user}");
                }
            }
        }
    }

    private void Application()
    {
        while (_isOnline)
        {
            Thread.Sleep(100);
            
            foreach (var (client, user) in Clients)
            {
                // Receive message
                var networkStream = client.GetStream();
                var buffer = new byte[1024];
                var bufferSize = networkStream.Socket.Available;
                if (bufferSize == 0) continue;
                
                // Return if message is null
                var size = networkStream.Read(buffer);
                if (size == 0) continue;

                // Decode request to string
                var json = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                var message = JsonSerializer.Deserialize<Message>(json)!;

                // To server message
                Console.WriteLine($"[Incoming Message]: {json}");
                switch (message.MessageType)
                {
                    case MessageType.Command:
                        var cmd = message.Text;
                        var cmdEndIndex = message.Text.IndexOf(' ');
                        if (cmdEndIndex != -1)
                        {
                            cmd = message.Text[..cmdEndIndex];
                        }
                        
                        // Get command method
                        if (_classChatApplication.Commands.TryGetValue(cmd, out var func))
                        {
                            var result = func(client, message.Text);
                            var group = user.Group;
                            var response = JsonSerializer.Serialize(new ServerResponse(1,
                                new Message(1, "SERVER", user.Name, result.Text, MessageType.Command), group));
                            
                            networkStream.Write(Encoding.UTF8.GetBytes(response));
                            Console.WriteLine($"[Outgoing Message]: {response}");
                        }
                        break;
                    case MessageType.GroupBroadcast:
                        // Find all group members
                        var members = new List<TcpClient>();
                        foreach (var (groupClient, groupUser) in Clients)
                        {
                            if (groupUser.Id == user.Id) continue;
                            
                            if (groupUser.Group == user.Group)
                            {
                                members.Add(groupClient);
                            }
                        }
                        
                        // Send message to group members
                        if (members.Any())
                        {
                            foreach (var member in members)
                            {
                                var stream = member.GetStream();
                                var group = Clients[member].Group;
                                var response = JsonSerializer.Serialize(new ServerResponse(1, message, group));
                                stream.Write(Encoding.UTF8.GetBytes(response));
                                Console.WriteLine($"[Outgoing Message]: {response}");
                            }
                        }
                        break;
                    default:
                        // Send message to target
                        var target = FindMessageTarget(message);
                        if (target != null)
                        {
                            var targetStream = target.GetStream();
                            var group = Clients[target].Group;
                            var response = JsonSerializer.Serialize(new ServerResponse(1, message, group));
                            targetStream.Write(Encoding.UTF8.GetBytes(response));
                            Console.WriteLine($"[Outgoing Message]: {response}");
                        }
                        break;
                }
                
                GC.Collect();
            }
        }
    }
    
    // Helpers
    private void TryRegisterClient(TcpListener listener)
    {
        // Accept a tcp client
        var tcpClient = listener.AcceptTcpClient();

        // Register client
        var networkStream = tcpClient.GetStream();
        var buffer = new byte[128];
        var size = networkStream.Read(buffer);
        
        // Return if message is null
        if (size == 0) return;

        // Decode message to string
        var message = Encoding.UTF8.GetString(buffer).TrimEnd('\0');

        // Check if registration request
        if (IsRegistrationRequest(message))
        {
            var jsonData = message.Replace("<user>", "");
            var user = JsonSerializer.Deserialize<User>(jsonData)!;

            // Add client to dictionary (client registrar)
            if (!Clients.ContainsKey(tcpClient))
            {
                Clients.Add(tcpClient, user);
                _classChatApplication.UserRegistry.Add(tcpClient, user);
                Console.WriteLine($"Client Connected: {user}");
                
                // Send client command listing
                var commandListingJson = JsonSerializer.Serialize(_classChatApplication.Commands.Keys.ToList());
                var encodedJson = Encoding.UTF8.GetBytes(commandListingJson);
                networkStream.Write(encodedJson);
            }
        }
    }

    private TcpClient? FindMessageTarget(Message message)
    {
        foreach (var (client, user) in Clients)
        {
            if (message.Receiver == user.Name)
            {
                return client;
            }
        }

        return null;
    }

    private static bool IsClientActive(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            stream.Write(ReadOnlySpan<byte>.Empty);
            return true;
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    private static bool IsRegistrationRequest(string message)
    {
        return message.StartsWith("<user>");
    }
}
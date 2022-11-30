using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClassChatAPI.Utility;

namespace ClassChatAPI.Core;

public sealed class ClassChatClient
{
    private readonly int _port;
    private readonly TcpClient _client;
    private string _group;
    private readonly List<string> _commands;

    public ClassChatClient(int port)
    {
        _port = port;
        _client = new TcpClient();
        _group = string.Empty;
        _commands = new List<string>();
    }

    public void Register(string username)
    {
        Console.Title = "Client";
        
        try
        {
            var ipHost = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = ipHost.AddressList[2];
            var localEndPoint = new IPEndPoint(ipAddress, _port);

            try
            {
                // Connect to Server endpoint socket
                _client.Connect(localEndPoint);
                
                // Register user to server
                var networkStream = _client.GetStream();
                var registrationJson =
                    $"<user>{{\"Id\":\"{_client.Client.LocalEndPoint}\",\"Name\":\"{username}\",\"Group\":\"\"}}<user>";
                networkStream.Write(Encoding.UTF8.GetBytes(registrationJson));
                
                // Receive valid command listing
                var buffer = new byte[512];
                var bufferSize = networkStream.Read(buffer);
                if (bufferSize == 0) throw new Exception("Could not retrieve command listing.");
                var jsonCommands = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                var commands = JsonSerializer.Deserialize<List<string>>(jsonCommands);

                if (commands != null)
                {
                    _commands.AddRange(commands);
                }

                // Display initialization messages
                Console.WriteLine($"Connection Established with ClassChat! [{_client.Client.RemoteEndPoint}]\n");
                RefreshScreen();

                // Read/Write network stream loop
                while (true)
                {
                    // User prompt
                    var groupTag = string.IsNullOrEmpty(_group) ? _group : $"{_group}@";
                    Console.Write($"{groupTag}{username}: ");

                    // WRITE TO SERVER
                    // Client command input
                    var groupHandle = string.IsNullOrEmpty(_group) ? _group : $"G@{_group}:";
                    var command = Console.ReadLine();
                    if (!string.IsNullOrEmpty(command))
                    {
                        var qualifiedCommand = $"{groupHandle}{command}";
                        
                        if (command == "ip") Console.WriteLine(_client.Client.LocalEndPoint);
                        else if (command == "clear") RefreshScreen();
                        else if (command == "exit") break;
                        else if (IsCommand(command))
                        {
                            try
                            {
                                SendCommand(networkStream, username, command);
                            }
                            catch (Exception e)
                            {
                            }
                        }
                        else if (ClassChatApplication.IsMessageRequest(command))
                        {
                            try
                            {
                                var segments = command.Split(' ', 2);
                                var receiver = segments[0][1..];
                                SendMessage(networkStream, username, receiver, segments[1], MessageType.SingleReceiver);
                            }
                            catch (Exception e)
                            {
                            }
                        }
                        else if (ClassChatApplication.IsGroupMessageRequest(qualifiedCommand))
                        {
                            try
                            {                                
                                var segments = qualifiedCommand.Split(':', 2);
                                var groupId = segments[0][2..];
                                SendMessage(networkStream, username, groupId, segments[1], MessageType.GroupBroadcast);
                            }
                            catch (Exception e)
                            {
                            }
                        }
                    }

                    // READ FROM SERVER
                    // Receive Server response
                    if (networkStream.DataAvailable)
                    {
                        var messageReceived = new byte[1024];
                        var size = networkStream.Read(messageReceived);
                        if (size == 0) continue;

                        // Receive and convert message from JSON
                        var json = Encoding.UTF8.GetString(messageReceived).TrimEnd('\0');
                        var (_, message, group) = JsonSerializer.Deserialize<ServerResponse>(json)!;

                        if (message != null)
                        {
                            switch (message.MessageType)
                            {
                                case MessageType.Command:
                                    // Display message
                                    Console.WriteLine(message.Text);
                                    break;
                                default:
                                    // Display message
                                    Console.WriteLine($"<{message.Sender}>: {message.Text}");
                                    break;
                            }
                        }

                        // Assign group
                        _group = group;
                    }
                }
                
                // Dispose of stream resources
                _client.Dispose();
                _client.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Error]: {e.Message}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }

    private static void RefreshScreen()
    {
        Console.Clear();
        Console.WriteLine("Welcome to ClassChat!");
        Console.WriteLine("=====================");
        Console.WriteLine("Type '@<user> <message>' to send a direct message to user.");
        Console.WriteLine("Type 'ip' to show client IP address.");
        Console.WriteLine("Type 'clear' to clear the screen.");
        Console.WriteLine("Type 'exit' to exit application.");
        Console.WriteLine("Type 'help' for more commands.");
        Console.WriteLine();
    }

    private bool IsCommand(string command)
    {
        try
        {
            var cmd = command;
            var endCommandIndex = command.IndexOf(' ');
            if (endCommandIndex != -1)
            {
                cmd = command[..endCommandIndex];
            }
            return _commands.Contains(cmd);
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    private void SendMessage(NetworkStream stream, string sender, string receiver, string message, MessageType messageType)
    {
        // Convert message to JSON and send to server
        var json = JsonSerializer.Serialize(new Message(1, sender, receiver, message, messageType));
        var messageSent = Encoding.UTF8.GetBytes(json);
        stream.Write(messageSent);
        
        // Allow cancel reception
        AwaitReceptionCancellation(stream);
    }
    
    private void SendCommand(NetworkStream stream, string sender, string command)
    {
        // Convert command to JSON and send to server
        var json = JsonSerializer.Serialize(new Message(1, sender, "SERVER", command, MessageType.Command));
        var messageSent = Encoding.UTF8.GetBytes(json);
        stream.Write(messageSent);
        
        // Allow cancel reception
        AwaitReceptionCancellation(stream);
    }

    private static void AwaitReceptionCancellation(NetworkStream stream)
    {
        var isCancelled = true;
        var cancelStallWorker = new Thread(() =>
        {
            Console.ReadKey();
            isCancelled = false;
        });
        
        cancelStallWorker.Start();
        while (!stream.DataAvailable && isCancelled) {}
    }
}
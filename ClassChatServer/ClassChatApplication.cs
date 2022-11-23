using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using ClassChatAPI.Core;
using ClassChatAPI.Users;
using ClassChatAPI.Utility;

namespace ClassChatAPI;

public sealed class ClassChatApplication
{
    public const int DefaultPort = 5000;
    public int CurrentPort { get; } = DefaultPort;
    public IDictionary<TcpClient, User> UserRegistry { get; } = new ConcurrentDictionary<TcpClient, User>();
    public ISet<string> Groups { get; } = new HashSet<string>();
    public IDictionary<string, Func<TcpClient, string, CommandResult>> Commands { get; }

    private readonly ClassChatServer _classChatServer;
    
    public ClassChatApplication()
    {
        _classChatServer = new ClassChatServer(this, CurrentPort);
        
        Commands = new Dictionary<string, Func<TcpClient, string, CommandResult>>()
        {
            { "help", ShowCommands },
            { "host", ShowHost },
            { "users", ShowUsers },
            { "joingroup", TryJoinGroup },
            { "leavegroup", TryLeaveGroup },
        };
    }

    public void Run()
    {
        _classChatServer.Run();
    }

    // Static Methods
    public static bool IsMessageRequest(string command)
    {
        return command.StartsWith('@');
    }
    
    public static bool IsGroupMessageRequest(string command)
    {
        return command.StartsWith("G@");
    }

    private CommandResult ShowCommands(TcpClient client, string cmd)
    {
        // Construct command list
        var builder = new StringBuilder();
        builder.AppendLine("'help' - Displays a list of all commands");
        builder.AppendLine("'host' - Displays the host IP address");
        builder.AppendLine("'users' - Displays a list of all active users");
        builder.AppendLine("'joingroup <group>' - Adds user to group, or creates group if not valid");
        builder.AppendLine("'leavegroup' - Removes user from current group");

        var messageText = builder.ToString();
        return new CommandResult { Result = true, Text = messageText};
    }
    
    private CommandResult ShowHost(TcpClient client, string cmd)
    {
        return new CommandResult { Result = true, Text = $"Host:[{_classChatServer.IpAddress}]" };
    }

    private CommandResult ShowUsers(TcpClient client, string cmd)
    {
        if (UserRegistry.Count <= 1) return new CommandResult { Result = true, Text = "You are the only active user at this time!" };

        // Construct users list
        var builder = new StringBuilder();
        builder.AppendLine("\nActive Users:");
        foreach (var user in UserRegistry.Values)
        {
            if (user.Id == UserRegistry[client].Id) continue;
            
            builder.AppendLine($"@{user.Name}");
        }

        var messageText = builder.ToString();
        return new CommandResult { Result = true, Text = messageText};
    }
    
    private CommandResult TryJoinGroup(TcpClient client, string cmd)
    {
        if (cmd.Length <= 10) return new CommandResult { Result = false, Text = "Invalid Group!"};

        var span = cmd.AsSpan();
        span = span[10..];
        var group = span.ToString();
        
        UserRegistry[client].Group = group;
        Groups.Add(group);

        return new CommandResult { Result = true, Text = $"Joined Group [{group}]"};
    }
    
    private CommandResult TryLeaveGroup(TcpClient client, string cmd)
    {
        var user = UserRegistry[client];
        if (string.IsNullOrEmpty(user.Group)) return new CommandResult {Result = true, Text = "You are not in a group!"};

        var oldGroup = user.Group;
        user.Group = string.Empty;
        return new CommandResult {Result = true, Text = $"You have left your group [{oldGroup}]"};
    }
}
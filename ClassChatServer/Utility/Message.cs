namespace ClassChatAPI.Utility;

public enum MessageType
{
    Command,
    SingleReceiver,
    GroupBroadcast
}

public sealed record Message(int Status, string Sender, string Receiver, string Text, MessageType MessageType);
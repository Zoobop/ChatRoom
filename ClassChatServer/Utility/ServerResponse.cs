namespace ClassChatAPI.Utility;

public sealed record ServerResponse(int Status, Message? Message, string Group);
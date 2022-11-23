namespace ClassChatAPI.Users;

public sealed class User
{
    public string Id { get; }
    public string Name { get; }
    public string Group { get; set; }

    public User(string id, string name, string group)
    {
        Id = id;
        Name = name;
        Group = group;
    }

    public override string ToString()
    {
        return $"{Name}";
    }
}
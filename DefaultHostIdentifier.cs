public class DefaultHostIdentifier : IHostIdentifier
{
    public string Address { get; private set; }

    public DefaultHostIdentifier(string address)
    {
        Address = address;
    }
}
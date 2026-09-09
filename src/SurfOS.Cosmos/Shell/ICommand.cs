namespace SurfOS.Shell
{
    public interface ICommand
    {
        string Name { get; }
        string Usage { get; }
        string Description { get; }
        void Execute(ParsedCommand command);
    }
}

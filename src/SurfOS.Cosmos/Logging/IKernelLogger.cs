namespace SurfOS.Logging
{
    public interface IKernelLogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }
}

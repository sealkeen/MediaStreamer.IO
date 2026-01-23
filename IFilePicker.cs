using System.Threading.Tasks;

namespace MediaStreamer.IO
{
    public interface IFilePicker
    {
        Task<string> PickFile();
    }
}

using System.Text;

namespace YtMusicTerminal.UI;

public sealed class TerminalSession : IDisposable
{
    private bool _entered;

    public void Enter()
    {
        if (_entered)
        {
            return;
        }

        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Write("\u001b[?1049h\u001b[2J\u001b[H\u001b[?25l\u001b[?7l");
        _entered = true;
    }

    public void Write(string frame)
    {
        Console.Write(frame);
    }

    public void Dispose()
    {
        if (!_entered)
        {
            return;
        }

        Console.Write("\u001b[0m\u001b[?7h\u001b[?25h\u001b[?1049l");
        _entered = false;
    }
}

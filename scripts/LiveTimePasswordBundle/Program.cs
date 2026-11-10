using System.Security.Cryptography;
using System.Text;
using IMRCRaceControl.Services;

if (args is not [var outputPath])
{
    Console.Error.WriteLine("Usage: LiveTimePasswordBundle <output-path>");
    return 2;
}

var password = new byte[LiveTimePasswordBundle.MaxPasswordBytes + 1];
var characters = new char[512];
try
{
    int length;
    if (Console.IsInputRedirected)
    {
        using var input = Console.OpenStandardInput();
        length = input.ReadAtLeast(password, password.Length, throwOnEndOfStream: false);
        if (length <= LiveTimePasswordBundle.MaxPasswordBytes && length > 0 && password[length - 1] == (byte)'\n')
        {
            length--;
            if (length > 0 && password[length - 1] == (byte)'\r') length--;
        }
    }
    else
    {
        Console.Error.Write("LiveTime backup passcode (input hidden): ");
        var count = 0;
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Escape) return 2;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (count > 0) characters[--count] = '\0';
            }
            else if (!char.IsControl(key.KeyChar))
            {
                if (count == characters.Length)
                {
                    Console.Error.WriteLine("\nPasscode exceeds the supported length.");
                    return 2;
                }
                characters[count++] = key.KeyChar;
            }
        }
        Console.Error.WriteLine();
        length = Encoding.UTF8.GetBytes(characters.AsSpan(0, count), password);
    }
    if (length is < 1 or > LiveTimePasswordBundle.MaxPasswordBytes)
    {
        Console.Error.WriteLine("Provide a nonempty LiveTime passcode within the supported length.");
        return 2;
    }
    using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    output.Write(LiveTimePasswordBundle.Wrap(password.AsSpan(0, length)));
    return 0;
}
finally
{
    Array.Clear(characters);
    CryptographicOperations.ZeroMemory(password);
}

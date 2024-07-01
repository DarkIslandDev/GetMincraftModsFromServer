using System.Security.Principal;
using Renci.SshNet;
using Renci.SshNet.Sftp;

public class Program
{
    private const string Host = "";
    private const int Port = 7477;
    private const string Username = "";
    private const string Password = "";
    private const string RemoteDirectory = "/mods/";
    private const string LocalDirectory = ".minecraft\\mods";

    private const int ConnectionTimeout = 180;

    private static async Task Main(string[] args)
    {
        if (!IsAdministrator())
        {
            Console.WriteLine("You need to run the application as administrator.");
            Exit();

            return;
        }

        CancellationTokenSource sourceToken = new CancellationTokenSource();
        CancellationToken token = sourceToken.Token;

        string localPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LocalDirectory);

        using (SftpClient client = new SftpClient(Host, Port, Username, Password))
        {
            try
            {
                async void TimerCallback(object? state)
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                        Console.WriteLine("Session timed out. Reconnecting...");
                        await client.ConnectAsync(token);
                        Console.WriteLine("Connection re-established.");
                    }
                }

                Timer connectionTimer = new Timer(
                    TimerCallback,
                    null,
                    TimeSpan.FromSeconds(ConnectionTimeout),
                    TimeSpan.FromSeconds(ConnectionTimeout)
                );


                await client.ConnectAsync(token);
                Console.WriteLine("Connection to SFTP server established.");

                await DownloadFiles(client, localPath, token);

                await DeleteExtraFiles(client, localPath, token);
                
                await connectionTimer.DisposeAsync();
                
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }
        }

        Exit();
    }

    private static void Exit()
    {
        Console.WriteLine("Operation completed. Press any key to exit.");
        Console.ReadKey();
    }

    private static async Task DownloadFiles(SftpClient client, string localPath, CancellationToken token)
    {
        List<ISftpFile> remoteFiles = await client.ListDirectoryAsync(RemoteDirectory, token).ToListAsync(token);

        Directory.CreateDirectory(localPath);

        foreach (ISftpFile remoteFile in remoteFiles)
        {
            if (remoteFile.Name.EndsWith(".jar"))
            {
                string localFileName = Path.Combine(localPath, remoteFile.Name);

                if (File.Exists(localFileName))
                {
                    Console.WriteLine($"File {remoteFile.Name} already exists, skipping downloading");
                    continue;
                }

                if (!client.IsConnected)
                {
                    await client.ConnectAsync(token);
                    Console.WriteLine("Connection to SFTP server re-established.");
                }

                await using (FileStream fileStream = File.Create(localFileName))
                {
                    Console.WriteLine($"Download file {remoteFile.Name} from: {Path.Combine(RemoteDirectory)}");
                    client.DownloadFile(Path.Combine(RemoteDirectory, remoteFile.Name), fileStream);
                }
            }
        }
    }

    private static async Task DeleteExtraFiles(SftpClient client, string localPath, CancellationToken token)
    {
        IEnumerable<string> localFiles = Directory.EnumerateFiles(localPath);
        List<string> remoteFiles = await GetRemoteFileNames(client, RemoteDirectory, token);

        foreach (var localFile in localFiles)
        {
            var fileName = Path.GetFileName(localFile);
            if (!remoteFiles.Contains(fileName))
            {
                Console.WriteLine($"Deleting extra file {fileName}");
                File.Delete(localFile);
            }
        }
    }

    private static async Task<List<string>> GetRemoteFileNames(SftpClient client, string path, CancellationToken token)
    {
        if (!client.IsConnected)
        {
            await client.ConnectAsync(token);
            Console.WriteLine("Connection to SFTP server re-established.");
        }

        var files = await client.ListDirectoryAsync(path, token).ToListAsync(token);
        return files.Select(f => f.Name).ToList();
    }

    private static bool IsAdministrator()
    {
        return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
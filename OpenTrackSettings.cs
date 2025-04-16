using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public class OpenTrackSettings : Window
{
    private const string ConfigFileName = "udp_config.json";

    public string IPAddress { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 4242;
    public string OpenTrackFolderPath { get; private set; } = "";

    private TextBox ipBox;
    private TextBox portBox;
    private TextBox folderBox;

    public OpenTrackSettings()
    {
        Width = 400;
        Height = 250;
        Title = "Enter Settings";

        ipBox = new TextBox();
        portBox = new TextBox();
        folderBox = new TextBox { IsReadOnly = true };

        var folderButton = new Button { Content = "Browse..." };
        var okButton = new Button { Content = "OK" };

        folderButton.Click += async (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Select OpenTrack Folder" };
            var result = await dialog.ShowAsync(this);
            if (!string.IsNullOrWhiteSpace(result))
            {
                OpenTrackFolderPath = result;
                folderBox.Text = result;
            }
        };

        okButton.Click += (_, _) =>
        {
            IPAddress = ipBox.Text;
            if (int.TryParse(portBox.Text, out var parsedPort))
                Port = parsedPort;

            Save();
            Close();
        };

        Content = new StackPanel
        {
            Margin = new Thickness(10),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "IP Address:" },
                ipBox,
                new TextBlock { Text = "Port:" },
                portBox,
                new TextBlock { Text = "OpenTrack Folder:" },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        folderBox,
                        folderButton
                    }
                },
                okButton
            }
        };

        // Load config when window is created
        LoadIntoFields();
    }

    private void LoadIntoFields()
    {
        var config = LoadOrDefault();
        IPAddress = config.IPAddress;
        Port = config.Port;
        OpenTrackFolderPath = config.OpenTrackFolderPath;

        ipBox.Text = IPAddress;
        portBox.Text = Port.ToString();
        folderBox.Text = OpenTrackFolderPath;
    }

    private void Save()
    {
        var config = new UdpConfig
        {
            IPAddress = this.IPAddress,
            Port = this.Port,
            OpenTrackFolderPath = this.OpenTrackFolderPath
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigFileName, json);
    }

    public static UdpConfig LoadOrDefault()
    {
        if (!File.Exists(ConfigFileName))
            return new UdpConfig(); // default values

        try
        {
            var json = File.ReadAllText(ConfigFileName);
            return JsonSerializer.Deserialize<UdpConfig>(json) ?? new UdpConfig();
        }
        catch
        {
            return new UdpConfig(); // fallback to default on error
        }
    }

    public class UdpConfig
    {
        public string IPAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 4242;
        public string OpenTrackFolderPath { get; set; } = "";
    }
}

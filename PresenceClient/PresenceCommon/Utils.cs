using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PresenceCommon;

public static class Utils
{
    private static readonly HttpClient Client = new();

    static Utils()
    {
        QuestOverrides = new Dictionary<string, OverrideInfo>();
        SwitchOverrides = new Dictionary<string, OverrideInfo>();
    }

    public static Dictionary<string, OverrideInfo> QuestOverrides { get; private set; }
    public static Dictionary<string, OverrideInfo> SwitchOverrides { get; private set; }

    public static async Task InitializeOverridesAsync()
    {
        try
        {
            var questJson = await Client.GetStringAsync(
                "https://raw.githubusercontent.com/Sun-Research-University/PresenceClient/master/Resource/QuestApplicationOverrides.json");
            QuestOverrides = JsonConvert.DeserializeObject<Dictionary<string, OverrideInfo>>(questJson);

            var switchJson = await Client.GetStringAsync(
                "https://raw.githubusercontent.com/Sun-Research-University/PresenceClient/master/Resource/SwitchApplicationOverrides.json");
            SwitchOverrides = JsonConvert.DeserializeObject<Dictionary<string, OverrideInfo>>(switchJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing overrides: {ex.Message}");
        }
    }

    public static async Task<byte[]> ReceiveExactlyAsync(Socket handler, int length = 628,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[length];
        var receivedLength = 0;
        while (receivedLength < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var nextLength = await handler.ReceiveAsync(
                    buffer.AsMemory(receivedLength, length - receivedLength),
                    SocketFlags.None,
                    cancellationToken);
                if (nextLength == 0) throw new SocketException((int)SocketError.ConnectionReset);
                receivedLength += nextLength;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SocketException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SocketException((int)(ex is TimeoutException ? SocketError.TimedOut : SocketError.SocketError));
            }
        }

        return buffer;
    }

    public class OverrideInfo
    {
        public string CustomName { get; set; }
        public string CustomPrefix { get; set; }
        public string CustomKey { get; set; }
    }
}

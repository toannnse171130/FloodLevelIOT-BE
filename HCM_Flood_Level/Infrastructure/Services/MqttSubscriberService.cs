using MQTTnet;
using MQTTnet.Client;
using System.Text;
using System.Text.Json;
using Core.DTOs;
using Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR;
using Infrastructure.Hubs;

/// <summary>
/// Hosted service subscribe MQTT (HiveMQ) và ghi sensor reading vào DB.
/// Triển khai IHostedService để host giữ reference suốt vòng đời app —
/// nếu để client là biến local, GC sẽ thu hồi và ngừng nhận message.
/// </summary>
public class MqttSubscriberService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    // Giữ client thành field để không bị Garbage Collector thu hồi → MQTT ngừng nhận message.
    private IMqttClient? _mqttClient;
    private MqttClientOptions? _options;
    // Cờ chặn vòng reconnect khi app đang shutdown.
    private bool _stopping;

    // Buffer giữ 10 MQTT message gần nhất để debug
    private static readonly LinkedList<string> _recentMessages = new();
    private static readonly object _lock = new();
    private const int MaxBufferSize = 10;

    public MqttSubscriberService(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    /// <summary>
    /// Lấy danh sách 10 MQTT message gần nhất (mới nhất trước).
    /// </summary>
    public static List<string> GetRecentMessages()
    {
        lock (_lock)
        {
            return _recentMessages.ToList();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var mqttConfig = _configuration.GetSection("Mqtt");
        var host = mqttConfig["Host"];

        // Nếu MQTT chưa được cấu hình, bỏ qua quá trình khởi tạo để không bị crash backend
        if (string.IsNullOrWhiteSpace(host))
        {
            Console.WriteLine("[MQTT Warning] MQTT Host is not configured in appsettings.json. Skipping MQTT initialization.");
            return;
        }

        var portConfig = mqttConfig["Port"];
        var port = (string.IsNullOrWhiteSpace(portConfig) || portConfig == "0") ? 8883 : int.Parse(portConfig);
        var username = mqttConfig["Username"];
        var password = mqttConfig["Password"];
        var topic = mqttConfig["Topic"];
        if (string.IsNullOrWhiteSpace(topic)) topic = "flood/+/telemetry";

        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port);

        if (!string.IsNullOrEmpty(username))
        {
            optionsBuilder.WithCredentials(username, password);
        }

        // HiveMQ Cloud requires TLS
        if (port == 8883)
        {
            optionsBuilder.WithTls(new MqttClientOptionsBuilderTlsParameters
            {
                UseTls = true,
                IgnoreCertificateChainErrors = false,
                IgnoreCertificateRevocationErrors = false,
                AllowUntrustedCertificates = false
            });
        }

        _options = optionsBuilder.Build();

        _mqttClient.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                Console.WriteLine($"[MQTT Received] Topic: {e.ApplicationMessage.Topic} | Payload: {payload}");

                // Lưu message vào buffer debug
                lock (_lock)
                {
                    _recentMessages.AddFirst($"[{DateTime.UtcNow:HH:mm:ss}] {payload}");
                    if (_recentMessages.Count > MaxBufferSize)
                        _recentMessages.RemoveLast();
                }

                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<MqttPayload>(payload, jsonOptions);

                if (data != null && !string.IsNullOrEmpty(data.DeviceId))
                {
                    using var scope = _serviceProvider.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ISensorReadingService>();
                    var dto = await service.HandleIncomingData(data);

                    // Broadcast processed reading to all connected SignalR clients
                    if (dto != null)
                    {
                        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<SensorHub>>();
                        await hubContext.Clients.All.SendAsync("ReceiveSensorReading", dto);
                    }
                }
                else
                {
                    Console.WriteLine("[MQTT Error] Deserialization failed or DeviceId is empty.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing MQTT message: {ex.Message}");
            }
        };

        _mqttClient.DisconnectedAsync += async e =>
        {
            if (_stopping) return; // app đang shutdown, không reconnect
            Console.WriteLine("MQTT Disconnected. Retrying in 5 seconds...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            try
            {
                if (!_stopping && _mqttClient != null && _options != null)
                {
                    await _mqttClient.ConnectAsync(_options);
                    await _mqttClient.SubscribeAsync(topic);
                    Console.WriteLine($"MQTT Reconnected and Subscribed to {topic}");
                }
            }
            catch
            {
                Console.WriteLine("MQTT Reconnection failed.");
            }
        };

        try
        {
            await _mqttClient.ConnectAsync(_options, cancellationToken);
            await _mqttClient.SubscribeAsync(topic, cancellationToken: cancellationToken);
            Console.WriteLine($"MQTT Connected to {host}:{port} and Subscribed to {topic}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MQTT Connection failed: {ex.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        if (_mqttClient != null)
        {
            try
            {
                if (_mqttClient.IsConnected)
                    await _mqttClient.DisconnectAsync();
            }
            catch
            {
                // ignore lỗi disconnect khi shutdown
            }
            _mqttClient.Dispose();
            _mqttClient = null;
        }
    }
}

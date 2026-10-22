
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class FastAPIAlphaReceiver : MonoBehaviour
{
    [Header("Connection")]
    public string serverUrl = "ws://127.0.0.1:8000/ws/unity";

    public float AlphaPower { get; private set; }
    public bool HasData { get; private set; }
    public string PredictedClass { get; private set; }

    private ClientWebSocket socket;
    private CancellationTokenSource cancellation;

    private readonly ConcurrentQueue<EEGMessage> messages =
        new ConcurrentQueue<EEGMessage>();

    private RehabArmController controller;

    [Serializable]
    private class EEGMessage
    {
        public float alpha_power;
        public string predicted_class;
        public string timestamp;
        public string source;
    }

    private void Start()
    {
        controller = FindObjectOfType<RehabArmController>();
        cancellation = new CancellationTokenSource();

        _ = ConnectAndReceive(cancellation.Token);
    }

    private async Task ConnectAndReceive(CancellationToken token)
    {
        try
        {
            socket = new ClientWebSocket();

            Debug.Log($"Connecting to FastAPI: {serverUrl}");

            await socket.ConnectAsync(new Uri(serverUrl), token);

            Debug.Log("Connected to FastAPI.");

            byte[] buffer = new byte[4096];

            while (socket.State == WebSocketState.Open &&
                   !token.IsCancellationRequested)
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            token
                        );

                        if (result.MessageType == WebSocketMessageType.Close)
                            return;

                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text)
                        continue;

                    string json = Encoding.UTF8.GetString(stream.ToArray());

                    try
                    {
                        EEGMessage message =
                            JsonUtility.FromJson<EEGMessage>(json);

                        if (message != null)
                            messages.Enqueue(message);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"Cannot parse FastAPI message: {ex.Message}"
                        );
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Нормальное завершение при остановке Unity.
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                Debug.LogError(
                    $"FastAPI connection error: {ex.Message}"
                );
            }
        }
    }

    private void Update()
    {
        while (messages.TryDequeue(out EEGMessage message))
        {
            if (message.predicted_class == "left_hand" ||
                message.predicted_class == "right_hand")
            {
                PredictedClass = message.predicted_class;

                // Сначала логируем полученное предсказание.
                Debug.Log(
                    $"EEG prediction: {PredictedClass} " +
                    $"(source: {message.source})"
                );

                // Затем передаём команду контроллеру.
                if (controller != null)
                {
                    controller.SetEEGCommand(PredictedClass);
                }
            }

            // alpha_power необязателен.
            // Не используем значение, если оно невалидно.
            if (message.alpha_power > 0f &&
                !float.IsNaN(message.alpha_power) &&
                !float.IsInfinity(message.alpha_power))
            {
                AlphaPower = message.alpha_power;
                HasData = true;

                if (controller != null)
                {
                    controller.SetEEGFeedback(AlphaPower);
                }
            }
        }
    }

    private void OnDestroy()
    {
        cancellation?.Cancel();

        if (socket != null)
        {
            socket.Abort();
            socket.Dispose();
            socket = null;
        }

        cancellation?.Dispose();
        cancellation = null;
    }
}

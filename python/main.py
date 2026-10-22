
import asyncio
import math
from datetime import datetime, timezone

from fastapi import FastAPI, WebSocket, WebSocketDisconnect

app = FastAPI(title="VR Rehabilitation API")

unity_clients: set[WebSocket] = set()
clients_lock = asyncio.Lock()

latest_data = {
    "alpha_power": None,
    "predicted_class": None,
    "timestamp": None,
    "source": "synthetic",
}


@app.get("/")
async def root():
    return {
        "message": "VR Rehabilitation API is running",
        "docs": "/docs",
    }


@app.get("/health")
async def health():
    return {"status": "ok"}


@app.get("/api/latest")
async def get_latest():
    return latest_data.copy()


async def broadcast(data: dict):
    async with clients_lock:
        clients = list(unity_clients)

    disconnected = []

    for client in clients:
        try:
            await client.send_json(data)
        except Exception:
            disconnected.append(client)

    if disconnected:
        async with clients_lock:
            for client in disconnected:
                unity_clients.discard(client)


@app.websocket("/ws/python")
async def python_stream(websocket: WebSocket):
    global latest_data

    await websocket.accept()
    print("Python connected")

    try:
        while True:
            data = await websocket.receive_json()

            message = {
                "alpha_power": latest_data["alpha_power"],
                "predicted_class": latest_data["predicted_class"],
                "timestamp": datetime.now(timezone.utc).isoformat(),
                "source": data.get("source", "synthetic"),
            }

            # Alpha power is optional.
            if "alpha_power" in data:
                try:
                    alpha = float(data["alpha_power"])
                except (TypeError, ValueError):
                    alpha = None

                if (
                    alpha is not None
                    and math.isfinite(alpha)
                    and alpha >= 0
                ):
                    message["alpha_power"] = alpha

            # A classifier command is optional.
            predicted_class = data.get("predicted_class")

            if predicted_class in ("left_hand", "right_hand"):
                message["predicted_class"] = predicted_class

            if (
                message["alpha_power"] is None
                and message["predicted_class"] is None
            ):
                continue

            latest_data = message
            await broadcast(message)

    except WebSocketDisconnect:
        print("Python disconnected")


@app.websocket("/ws/unity")
async def unity_stream(websocket: WebSocket):
    await websocket.accept()

    async with clients_lock:
        unity_clients.add(websocket)

    print("Unity connected")

    try:
        await websocket.send_json(latest_data.copy())

        while True:
            await websocket.receive_text()

    except WebSocketDisconnect:
        pass
    finally:
        async with clients_lock:
            unity_clients.discard(websocket)

        print("Unity disconnected")

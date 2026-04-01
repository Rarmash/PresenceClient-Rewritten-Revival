import argparse
import json
import re
import socket
import struct
import time
from dataclasses import dataclass
from typing import Optional

import requests
from pypresence import Presence


TCP_PORT = 0xCAFE
PACKETMAGIC = 0xFFAADD23
PACKET_STRUCT = struct.Struct("<QQ612s")
HOME_MENU_PROGRAM_ID = 0x0100000000001000
SOCKET_TIMEOUT_SECONDS = 15
RECONNECT_DELAY_SECONDS = 2
OVERRIDES_BASE_URL = "https://raw.githubusercontent.com/Sun-Research-University/PresenceClient/master/Resource"


parser = argparse.ArgumentParser()
parser.add_argument("ip", help="The IP address of your device")
parser.add_argument("client_id", help="The Client ID of your Discord Rich Presence application")
parser.add_argument(
    "--ignore-home-screen",
    dest="ignore_home_screen",
    action="store_true",
    help="Don't display the home screen. Defaults to false if missing this flag.",
)
parser.add_argument(
    "--debug",
    action="store_true",
    help="Print packet and RPC debug information.",
)


def log(message: str) -> None:
    print(f"[{time.strftime('%H:%M:%S')}] {message}", flush=True)


def debug_log(enabled: bool, message: str) -> None:
    if enabled:
        log(message)


def load_overrides(debug: bool) -> tuple[dict, dict]:
    quest_url = f"{OVERRIDES_BASE_URL}/QuestApplicationOverrides.json"
    switch_url = f"{OVERRIDES_BASE_URL}/SwitchApplicationOverrides.json"
    try:
        quest_response = requests.get(quest_url, timeout=10)
        quest_response.raise_for_status()
        switch_response = requests.get(switch_url, timeout=10)
        switch_response.raise_for_status()
        debug_log(debug, "Override files downloaded successfully.")
        return quest_response.json(), switch_response.json()
    except Exception as exc:
        log(f"Failed to retrieve override files: {exc}")
        return {}, {}


@dataclass
class Title:
    magic: int
    pid: int
    name: str

    @classmethod
    def from_raw(cls, raw_data: bytes, quest_overrides: dict, switch_overrides: dict) -> "Title":
        magic, program_id, encoded_name = PACKET_STRUCT.unpack(raw_data)
        if program_id == 0:
            title = cls(magic=magic, pid=HOME_MENU_PROGRAM_ID, name="Home Menu")
        else:
            decoded_name = encoded_name.decode("utf-8", "ignore").split("\x00")[0].strip()
            title = cls(magic=magic, pid=program_id, name=decoded_name or "A Game")

        switch_key = f"0{title.pid:x}"
        if title.magic == PACKETMAGIC:
            if switch_key in switch_overrides:
                custom_name = switch_overrides[switch_key].get("CustomName")
                if custom_name:
                    title.name = custom_name
        else:
            if title.name in quest_overrides:
                custom_name = quest_overrides[title.name].get("CustomName")
                if custom_name:
                    title.name = custom_name

        return title


def validate_ip(ip: str) -> bool:
    regex = re.compile(
        r"^(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\."
        r"(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\."
        r"(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\."
        r"(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)$"
    )
    return regex.fullmatch(ip) is not None


def icon_from_pid(pid: int) -> str:
    return f"0{pid:x}"


def connect_rpc(client_id: str) -> Presence:
    rpc = Presence(str(client_id))
    rpc.connect()
    rpc.clear()
    return rpc


def connect_switch(switch_ip: str) -> socket.socket:
    switch_server_address = (switch_ip, TCP_PORT)
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(SOCKET_TIMEOUT_SECONDS)
    sock.connect(switch_server_address)
    return sock


def recv_exactly(sock: socket.socket, length: int) -> bytes:
    chunks = bytearray()
    while len(chunks) < length:
        chunk = sock.recv(length - len(chunks))
        if not chunk:
            raise ConnectionError("Connection closed by peer")
        chunks.extend(chunk)
    return bytes(chunks)


def build_rpc_payload(title: Title, quest_overrides: dict, switch_overrides: dict) -> dict:
    payload = {
        "details": "Nintendo Switch",
        "state": title.name,
        "large_text": title.name,
    }

    switch_key = f"0{title.pid:x}"
    if switch_key in switch_overrides:
        override_info = switch_overrides[switch_key]
        payload["large_image"] = override_info.get("CustomKey") or switch_key
    else:
        payload["large_image"] = switch_key

    if title.name == "Home Menu":
        payload["large_image"] = "home-menu"
        payload["state"] = "Home Menu"

    payload["small_text"] = "SwitchPresence-Rewritten"

    if title.name in quest_overrides:
        override_info = quest_overrides[title.name]
        payload["large_image"] = override_info.get("CustomKey") or payload["large_image"]

    return payload


def ensure_connection(switch_ip: str, debug: bool) -> socket.socket:
    while True:
        try:
            sock = connect_switch(switch_ip)
            log(f"Connected to {switch_ip}:{TCP_PORT}")
            return sock
        except Exception as exc:
            log(f"Switch connection failed: {exc}. Retrying in {RECONNECT_DELAY_SECONDS}s...")
            time.sleep(RECONNECT_DELAY_SECONDS)
            debug_log(debug, "Retrying Switch socket connect.")


def main() -> None:
    console_args = parser.parse_args()

    if not validate_ip(console_args.ip):
        log("Invalid IP")
        raise SystemExit(1)

    quest_overrides, switch_overrides = load_overrides(console_args.debug)

    try:
        rpc = connect_rpc(console_args.client_id)
        log("Discord RPC connected.")
        log("STATUS: Client running")
    except Exception as exc:
        log(f"Unable to start RPC: {exc}")
        raise SystemExit(2)

    last_program_name = ""
    start_timer: Optional[int] = None
    sock = ensure_connection(console_args.ip, console_args.debug)

    while True:
        try:
            raw_packet = recv_exactly(sock, PACKET_STRUCT.size)
            title = Title.from_raw(raw_packet, quest_overrides, switch_overrides)
            debug_log(
                console_args.debug,
                f"Packet magic=0x{title.magic:X} pid=0x{title.pid:016X} name={title.name}",
            )

            if title.magic != PACKETMAGIC:
                log(f"Invalid packet magic: 0x{title.magic:X}. Clearing presence.")
                rpc.clear()
                continue

            if last_program_name != title.name:
                start_timer = int(time.time())

            if console_args.ignore_home_screen and title.name == "Home Menu":
                rpc.clear()
                debug_log(console_args.debug, "Presence cleared because Home Menu is ignored.")
                log("GAME: Hidden")
            else:
                payload = build_rpc_payload(title, quest_overrides, switch_overrides)
                if start_timer is not None:
                    payload["start"] = start_timer
                rpc.update(**payload)
                debug_log(console_args.debug, f"rpc.update({json.dumps(payload, ensure_ascii=False)})")
                if last_program_name != title.name:
                    log(f"GAME: {title.name}")

            last_program_name = title.name
        except KeyboardInterrupt:
            log("Stopping client.")
            break
        except Exception as exc:
            log(f"Connection/RPC error: {exc}")
            try:
                sock.close()
            except Exception:
                pass
            time.sleep(RECONNECT_DELAY_SECONDS)
            sock = ensure_connection(console_args.ip, console_args.debug)

    try:
        rpc.clear()
        rpc.close()
    except Exception:
        pass
    try:
        sock.close()
    except Exception:
        pass


if __name__ == "__main__":
    main()

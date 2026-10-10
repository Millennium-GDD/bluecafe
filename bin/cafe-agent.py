#!/usr/bin/env python3
"""Send one versioned command to the running standalone BlueCafe app."""
import argparse
import json
import os
from pathlib import Path
import socket
import struct
import sys
import uuid

def receive(connection, length):
    chunks = bytearray()
    while len(chunks) < length:
        part = connection.recv(length - len(chunks))
        if not part:
            raise RuntimeError('BlueCafe closed the connection')
        chunks.extend(part)
    return chunks

def default_discovery():
    """Match Unity's default persistentDataPath; custom --data-path needs --discovery."""
    if sys.platform == 'win32':
        return Path(os.environ.get('USERPROFILE', str(Path.home()))) / 'AppData/LocalLow/BlueCafe/BlueCafe/agent.json'
    if sys.platform == 'darwin':
        return Path.home() / 'Library/Application Support/BlueCafe/BlueCafe/agent.json'
    return Path(os.environ.get('XDG_CONFIG_HOME', str(Path.home() / '.config'))) / 'unity3d/BlueCafe/BlueCafe/agent.json'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command')
    parser.add_argument('--arguments', default='{}', help='JSON command arguments')
    parser.add_argument('--revision', type=int, default=-1)
    parser.add_argument('--discovery', type=Path, default=default_discovery(), help='Agent discovery file; override for a custom app data directory')
    args = parser.parse_args()
    info = json.loads(args.discovery.read_text())
    request = dict(version=1, requestId=uuid.uuid4().hex, token=info['token'], command=args.command,
                   expectedRevision=args.revision, arguments=json.loads(args.arguments))
    data = json.dumps(request, allow_nan=False).encode()
    if len(data) > 4 * 1024 * 1024:
        parser.error('Request exceeds 4 MiB')
    with socket.create_connection(('127.0.0.1', info['port']), timeout=35) as conn:
        conn.sendall(struct.pack('!I', len(data)) + data)
        length = struct.unpack('!I', receive(conn, 4))[0]
        if length > 4 * 1024 * 1024:
            raise RuntimeError('Response exceeds 4 MiB')
        result = json.loads(receive(conn, length))
    print(json.dumps(result, indent=2))
    return 0 if result.get('success') else 1

if __name__ == '__main__':
    sys.exit(main())

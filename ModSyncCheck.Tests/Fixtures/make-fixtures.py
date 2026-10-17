"""Creates the .7z and .rar test archives in this folder.

Each archive has the same two mods, like a real mod download: `Mod` (with a config.json, which installs
must skip) and `Other`. The .7z is made by 7-Zip, so it's a real compressed and solid archive. RAR can only
be created by WinRAR, so the .rar files are written here directly in the RAR 4 and RAR 5 formats, with
the files stored uncompressed.

Usage: python make-fixtures.py [path to 7z.exe]
"""

import os
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib

FOLDER = os.path.dirname(os.path.abspath(__file__))

FILES = [
    ("Mod/manifest.json", b'{ "Name": "Mod", "UniqueID": "Someone.Mod", "Version": "1.2.0" }'),
    ("Mod/content.json", b'{ "Format": "2.0.0", "Changes": [] }'),
    ("Mod/assets/data.txt", b"some mod data"),
    ("Mod/config.json", b'{ "Enabled": true }'),
    ("Other/manifest.json", b'{ "Name": "Other", "UniqueID": "Someone.Other", "Version": "2.0.0" }'),
    ("Other/readme.txt", b"another mod in the same archive"),
]

# a fixed time, so the archives are the same each time they're made
DOS_TIME = (2026 - 1980) << 25 | 10 << 21 | 16 << 16 | 12 << 11  # 2026-10-16 12:00:00
UNIX_TIME = 1792152000


def crc32(data: bytes) -> int:
    return zlib.crc32(data) & 0xFFFFFFFF


def make_7z(seven_zip: str) -> None:
    path = os.path.join(FOLDER, "Mods.7z")
    if os.path.exists(path):
        os.remove(path)
    with tempfile.TemporaryDirectory() as temp:
        for name, content in FILES:
            file = os.path.join(temp, *name.split("/"))
            os.makedirs(os.path.dirname(file), exist_ok=True)
            with open(file, "wb") as f:
                f.write(content)
            os.utime(file, (UNIX_TIME, UNIX_TIME))
        subprocess.run([seven_zip, "a", "-t7z", "-ms=on", "-mtm=off", "-mtc=off", "-mta=off", path, "Mod", "Other"], cwd=temp, check=True, stdout=subprocess.DEVNULL)


def make_rar4() -> None:
    def block(head_type: int, flags: int, body: bytes, data: bytes = b"") -> bytes:
        header = struct.pack("<BHH", head_type, flags, 7 + len(body)) + body
        return struct.pack("<H", crc32(header) & 0xFFFF) + header + data

    out = b"Rar!\x1a\x07\x00"
    out += block(0x73, 0x0000, struct.pack("<HI", 0, 0))  # archive header
    for name, content in FILES:
        name_bytes = name.replace("/", "\\").encode("ascii")
        body = struct.pack(
            "<IIBIIBBHI",
            len(content),  # packed size
            len(content),  # unpacked size
            2,  # host OS: Windows
            crc32(content),
            DOS_TIME,
            20,  # version needed to extract (2.0)
            0x30,  # method: store
            len(name_bytes),
            0x20,  # attributes: archive
        ) + name_bytes
        out += block(0x74, 0x8000, body, content)  # file header, 0x8000 = has data after the header
    out += block(0x7B, 0x4000, b"")  # end of archive
    with open(os.path.join(FOLDER, "Mods-rar4.rar"), "wb") as f:
        f.write(out)


def make_rar5() -> None:
    def vint(value: int) -> bytes:
        out = bytearray()
        while True:
            byte = value & 0x7F
            value >>= 7
            out.append(byte | (0x80 if value else 0))
            if not value:
                return bytes(out)

    def block(fields: bytes, data: bytes = b"") -> bytes:
        header = vint(len(fields)) + fields
        return struct.pack("<I", crc32(header)) + header + data

    out = b"Rar!\x1a\x07\x01\x00"
    out += block(vint(1) + vint(0) + vint(0))  # main archive header: type 1, no flags, no archive flags
    for name, content in FILES:
        name_bytes = name.encode("utf-8")
        fields = (
            vint(2)  # type: file header
            + vint(0x0002)  # flags: has a data area
            + vint(len(content))  # data size
            + vint(0x0002 | 0x0004)  # file flags: has a modification time and a CRC32
            + vint(len(content))  # unpacked size
            + vint(0x20)  # attributes: archive
            + struct.pack("<I", UNIX_TIME)
            + struct.pack("<I", crc32(content))
            + vint(0)  # compression: version 0, not solid, method 0 (store)
            + vint(0)  # host OS: Windows
            + vint(len(name_bytes))
            + name_bytes
        )
        out += block(fields, content)
    out += block(vint(5) + vint(0) + vint(0))  # end of archive
    with open(os.path.join(FOLDER, "Mods-rar5.rar"), "wb") as f:
        f.write(out)


if __name__ == "__main__":
    seven_zip = sys.argv[1] if len(sys.argv) > 1 else shutil.which("7z") or r"C:\Program Files\7-Zip\7z.exe"
    make_7z(seven_zip)
    make_rar4()
    make_rar5()

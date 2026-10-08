# Controller link protocol (version 1)

Between VrFsim Controller Connect (Windows) and VrFsim on the Quest
(`Assets/VrFsim/Scripts/Runtime/Input/RemoteGamepad.cs` in the VrFsim repo).

- **Transport:** TCP. The game listens on `127.0.0.1:47812` on the headset (loopback only).
  Controller Connect runs `adb forward tcp:47812 tcp:47812` and connects to `127.0.0.1:47812` on the PC.
- **Handshake:** the PC sends the 4 ASCII bytes `VFG1`; the game answers `OK`. No answer means the
  game is not running yet (adb accepts the connection even then).
- **Packets:** 16 bytes, little-endian, sent when the state changes and at least every 100 ms
  (the game drops the connection after 3 s of silence).

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | `'S'` |
| 1 | 1 | reserved (0) |
| 2 | 2 | buttons, uint16: bit *i* = Unity `GamepadButton` *i* (0 D-pad up, 1 down, 2 left, 3 right, 4 North/Y, 5 East/B, 6 South/A, 7 West/X, 8 left stick press, 9 right stick press, 10 LB, 11 RB, 12 Start, 13 Select/Back) |
| 4 | 2 | left stick X, int16, −32767…32767 |
| 6 | 2 | left stick Y, int16, up is positive |
| 8 | 2 | right stick X, int16 |
| 10 | 2 | right stick Y, int16, up is positive |
| 12 | 1 | left trigger, 0…255 |
| 13 | 1 | right trigger, 0…255 |
| 14 | 2 | reserved (0) |

On disconnect the game removes its virtual gamepad.

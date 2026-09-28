# Loquendo TTS 7.14 Remote API — findings from supplied JARs

Static inspection of the supplied Java archives found an official Java RMI surface, separate from the unstable direct Win32 FFI experiment.

## Confirmed endpoint

`LTTS7DirectorManager.jar` identifies the manager as **Loquendo TTS Director Manager 7.14.0**. Its bytecode uses:

- default TCP/RMI port: `1099`
- RMI binding name: `LoquendoTTSEngineServer`
- URL shape: `rmi://<host>:<port>/LoquendoTTSEngineServer`

The manager starts its local administrator on `127.0.0.1:1099` when no other administrator is already present.

## Confirmed remote methods

`LTTS7EngineServerClient.jar` contains `loquendo.tts.engineserver.TTSEngineServer`, an RMI interface. Relevant calls include:

- `ping()`
- `getVersion()`
- `registerClient()` / `unregisterClient()`
- `GetVoicesAndComments()`
- `GetLanguages()` / `GetFrequencies()` / `GetCodings()`
- `checkVoicePresence()`
- `read(String, TTSParams, boolean, String)`
- `saveAudioOnServer(String, TTSParams, String)`
- `getOutBuffer(String)` / `getOutBuffer(String, int)`
- `isReading(String)` / `stop(String)`
- `getPhoneticTranscription(...)`

## TTSParams

The same JAR exposes serializable `TTSParams` with properties for:

- voice / language
- frequency / coding / channels
- volume / pitch / speed / timbre (+ percentage variants)
- gain / delay / balance
- text format / encoding
- voice flavour
- audio destination name/device/path
- graphic EQ
- phonetic alphabet

Constructor defaults observed in bytecode include `Ludoviko`, `Italian`, `44100`, `linear`, `autodetect`, `utf8`, stereo, prosody values 50, and phonetic alphabet `xsampa`.

## Design decision

The Remote API is a strong candidate for `loquendo7-remote`: it avoids loading `LoqTTS7.dll` into our managed bridge and delegates native lifetime/ABI details to Loquendo's own Engine Server. The direct native provider remains useful for diagnostics and possibly a later optimized backend.

The next implementation step needs the installed Remote API runtime/classpath (especially `TTSDirector.jar`, `LTTS7EngineServer.jar`, `LTTS7EngineServerAdministrator.jar`, and their utility JARs) or automatic discovery of that directory on the test PC.

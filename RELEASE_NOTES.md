# Release Notes - KaiROS AI v1.0.4

## 🚀 New Features

### Custom Model Support

- ➕ Add your own `.gguf` models from local files or download URLs
- 📦 SQLite database stores custom model entries persistently
- 🗑️ Delete custom models with one click

### Intel Arc & Vulkan Support

- 🎮 **Added Vulkan Backend**: High-performance acceleration specifically for Intel Arc and AMD GPUs.
- 🛠️ **Cross-Platform Stability**: Vulkan provides better compatibility than DirectML for universal Windows GPU support.
- 📏 **Arc VRAM Detection**: Correctly identifies VRAM for Intel Arc A770, A750, and other models.
- 📊 **Improved Status Bar**: Clearer hardware info with unified backend status.

### API Mode Enhancements

- 🌐 Added `internetClient` and `internetClientServer` capabilities
- 🔌 Improved API stability

### RAG Document Support

- 📄 Enhanced debug logging for document loading
- 🔍 Better context retrieval tracking
- 📝 Support for PDF, Word, and text files

---

## 🐛 Bug Fixes

- Fixed: Execution Backend UI wasn't applying selection
- Fixed: "Loading on GPU" text showed regardless of backend selection
- Fixed: Radio buttons for backend selection weren't working
- Fixed: MessageBox and OpenFileDialog ambiguity errors

---

## 📦 Technical Changes

- Added `Microsoft.Data.Sqlite` for custom model persistence
- Updated `IHardwareDetectionService` with `SetSelectedBackend()` method
- Added comprehensive debug logging for RAG pipeline
- Manifest now includes network capabilities

---

**Full Changelog:** v1.0.3 → v1.0.4

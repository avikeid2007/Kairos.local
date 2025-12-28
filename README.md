# KaiROS AI

<p align="center">
  <img src="KaiROS.AI/Assets/logo.png" alt="KaiROS AI Logo" width="128"/>
</p>

<p align="center">
  <b>A powerful local AI assistant for Windows</b><br>
  Run LLMs locally with GPU acceleration • No cloud required • Privacy-first
</p>

<p align="center">
  <a href="https://github.com/avikeid2007/Kairos.local/releases/latest"><img src="https://img.shields.io/github/v/release/avikeid2007/Kairos.local?style=flat-square&logo=github&label=Download" alt="Download"/></a>
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 9"/>
  <img src="https://img.shields.io/badge/WPF-Desktop-0078D4?style=flat-square&logo=windows" alt="WPF"/>
  <img src="https://img.shields.io/badge/CUDA-12-76B900?style=flat-square&logo=nvidia" alt="CUDA 12"/>
  <img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="MIT License"/>
</p>

---

## 📥 Download

<p align="center">
  <a href="https://github.com/avikeid2007/Kairos.local/releases/latest">
    <img src="https://img.shields.io/badge/Download_Installer-EXE-blue?style=for-the-badge&logo=windows" alt="Download Installer"/>
  </a>
</p>

- **[Download Latest Release](https://github.com/avikeid2007/Kairos.local/releases/latest)** - Self-contained Windows installer
- No .NET installation required
- Supports Windows 10/11 (x64)

---

## ✨ Features

- 🤖 **Run LLMs Locally** - No internet required after model download
- 🚀 **GPU Acceleration** - CUDA 12 support for NVIDIA GPUs
- 📦 **Model Catalog** - Pre-configured models from Hugging Face
- ⬇️ **Download Manager** - Pause, resume, and manage model downloads
- 💬 **Chat Interface** - Clean, modern UI with streaming responses
- 📊 **Performance Stats** - Real-time tokens/sec and memory usage
- 🎨 **Modern Dark Theme** - Beautiful gradient-based UI design
- 🔧 **Hardware Detection** - Automatic CPU/GPU/NPU detection

## 📸 Screenshots

| Model Catalog | Chat Interface | Settings |
|:---:|:---:|:---:|
| Download and manage AI models | Chat with streaming responses | Configure hardware backend |

## 🚀 Getting Started

### Prerequisites

- **Windows 10/11** (x64)
- **.NET 9 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
- **CUDA Toolkit 12** (optional, for GPU acceleration) - [Download](https://developer.nvidia.com/cuda-downloads)

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/KaiROS.AI.git
   cd KaiROS.AI
   ```

2. **Restore packages and build**
   ```bash
   dotnet restore
   dotnet build --configuration Release
   ```

3. **Run the application**
   ```bash
   dotnet run --project KaiROS.AI
   ```

### First Run

1. Open the **Models** tab
2. Click **Download** on your preferred model (TinyLlama recommended for testing)
3. Once downloaded, click **Load Model**
4. Navigate to **Chat** and start chatting!

## 📦 Included Models

| Model | Size | RAM Required | Best For |
|-------|------|--------------|----------|
| TinyLlama 1.1B | 0.8 GB | 2 GB | Quick responses, testing |
| Phi-3 Mini 3.8B ⭐ | 2.2 GB | 4 GB | General conversations |
| Phi-2 2.7B | 1.6 GB | 4 GB | Coding tasks |
| LLaMA 3.2 3B | 1.9 GB | 4 GB | Multilingual |
| Mistral 7B ⭐ | 4.4 GB | 8 GB | Complex tasks |
| LLaMA 3.1 8B | 4.9 GB | 12 GB | Advanced reasoning |
| Gemma 2 9B | 5.4 GB | 12 GB | Premium quality |

⭐ = Recommended

## 🛠️ Tech Stack

- **Framework**: .NET 9 + WPF
- **LLM Runtime**: [LLamaSharp](https://github.com/SciSharp/LLamaSharp)
- **MVVM**: [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- **GPU Support**: CUDA 12, DirectML
- **Model Format**: GGUF (llama.cpp compatible)

## 📁 Project Structure

```
KaiROS.AI/
├── Assets/              # App icons and images
├── Converters/          # XAML value converters
├── Models/              # Data models
├── Services/            # Business logic
│   ├── ChatService.cs           # LLM inference
│   ├── DownloadService.cs       # File downloads
│   ├── HardwareDetectionService.cs
│   └── ModelManagerService.cs   # Model catalog
├── Themes/              # UI styling
├── ViewModels/          # MVVM ViewModels
├── Views/               # XAML views
└── appsettings.json     # Model catalog config
```

## ⚙️ Configuration

### Adding Custom Models

Edit `appsettings.json` to add your own models:

```json
{
  "LLMModels": [
    {
      "Name": "your-model.gguf",
      "DisplayName": "Your Model Name",
      "Description": "Description here",
      "SizeText": "2.0 GB",
      "SizeBytes": 2147483648,
      "DownloadUrl": "https://huggingface.co/...",
      "MinRam": "4 GB",
      "Category": "small"
    }
  ]
}
```

### GPU Configuration

The app auto-detects available backends. To force a specific backend:

1. Go to **Settings**
2. Select your preferred **Execution Backend**
3. Reload your model

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [LLamaSharp](https://github.com/SciSharp/LLamaSharp) - .NET bindings for llama.cpp
- [Hugging Face](https://huggingface.co/) - Model hosting
- [TheBloke](https://huggingface.co/TheBloke) - GGUF model quantizations

---

<p align="center">
  Made with ❤️ for local AI enthusiasts
</p>

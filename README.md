# 🎵 TID3 - Advanced ID3 Tag Editor

<div align="center">

![TID3 Logo](https://img.shields.io/badge/TID3-ID3%20Tag%20Editor-blue?style=for-the-badge&logo=music)

**Professional ID3 tag editor with batch processing and online database integration**

[![.NET](https://img.shields.io/badge/.NET-9.0-5C2D91?style=flat-square&logo=.net)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-11%20Ready-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows/)
[![WPF](https://img.shields.io/badge/WPF-Modern%20UI-2D5699?style=flat-square)](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

</div>

## ✨ Features

### 🎯 Core Functionality
- **Multi-format Support**: MP3, FLAC, M4A, AAC, WAV, WMA
- **Batch Processing**: Edit multiple files simultaneously
- **Advanced Tag Comparison**: Visual before/after comparison with change tracking
- **Smart Auto-Detection**: Automatic best match selection with confidence scoring
- **Backup & Safety**: Optional backup creation before modifications

### 🌐 Online Database Integration
- **MusicBrainz Integration**: Access to comprehensive music database
- **Discogs Support**: Professional discography database
- **Intelligent Matching**: Advanced scoring algorithm considering:
  - Artist similarity (35% weight)
  - Album/Title matching (30% weight)
  - **Track count comparison** (20% weight)
  - Release year (10% weight)
  - Track titles (5% weight)
- **Smart Field Preservation**: Respects album-level vs track-level metadata
- **Auto-Selection**: Automatically applies matches with 70%+ confidence

### 🎨 Modern Windows 11 UI
- **Dark Theme**: Professional dark interface
- **Windows 11 Scrollbars**: Thin, modern scrollbars matching system design
- **Responsive Design**: Optimized for different screen sizes
- **Intuitive Navigation**: Tab-based interface with clear sections
- **Real-time Preview**: Live comparison view of changes

### ⚙️ Advanced Settings
- **API Configuration**: MusicBrainz and Discogs credentials
- **File Processing Options**: Auto-save, backup creation, subdirectory inclusion
- **UI Customization**: Theme selection, font size, language options
- **Performance Tuning**: Concurrent operations control
- **Cache Management**: Efficient data caching system

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- .NET 9.0 Runtime
- Audio files in supported formats

### Installation
1. Download the latest release from [Releases](../../releases)
2. Extract the ZIP file to your preferred location
3. Run `TID3.exe`

### First Steps
1. **Load Files**: Use "Load Files" or "Load Folder" to import your audio collection
2. **Configure APIs**: Set up MusicBrainz and Discogs credentials in Settings
3. **Search Online**: Use the online database search for automatic tag completion
4. **Review Changes**: Check the Tag Comparison tab to review proposed changes
5. **Apply & Save**: Accept changes and save your files

## 📊 Project Status

### ✅ Completed Features
- [x] Core tag editing functionality
- [x] Multi-format audio file support
- [x] MusicBrainz API integration
- [x] Discogs API integration
- [x] Advanced matching algorithm with track count comparison
- [x] Tag comparison system with change tracking
- [x] Windows 11 modern UI design
- [x] Settings management with persistence
- [x] Batch processing capabilities
- [x] Auto-selection with confidence scoring
- [x] Smart field preservation for online metadata

### 🔧 Technical Implementation
- **Architecture**: MVVM pattern with nullable reference types
- **JSON Processing**: System.Text.Json for API responses
- **Audio Processing**: TagLibSharp for metadata handling
- **String Matching**: Levenshtein distance algorithm
- **UI Framework**: WPF with modern Windows 11 styling
- **Configuration**: JSON-based settings with validation

### 📈 Recent Improvements
- **Enhanced Matching**: Added track count comparison to scoring algorithm
- **UI Polish**: Implemented Windows 11 style scrollbars throughout application
- **Bug Fixes**: Resolved tag comparison display issues and OnlineSourceComboBox errors
- **Performance**: Optimized online database search and result processing
- **UX**: Improved automatic selection and comparison tab switching

## 🛠️ Development

### Building from Source
```bash
git clone https://github.com/yourusername/TID3.git
cd TID3
dotnet restore
dotnet build
```

### Requirements
- Visual Studio 2022 or VS Code
- .NET 9.0 SDK
- Windows 10/11 for WPF development

### Architecture Overview
```
TID3/
├── Models/           # Data models and entities
├── Services/         # API and tag processing services
├── Views/           # WPF windows and user controls
├── Resources/       # Styles and application resources
└── Utils/           # Helper classes and utilities
```

## 🎵 Supported Formats

| Format | Read | Write | Notes |
|--------|------|-------|-------|
| MP3    | ✅   | ✅    | Full ID3v2 support |
| FLAC   | ✅   | ✅    | Vorbis comments |
| M4A    | ✅   | ✅    | iTunes metadata |
| AAC    | ✅   | ✅    | Standard tags |
| WAV    | ✅   | ✅    | ID3v2 in WAV |
| WMA    | ✅   | ✅    | Windows Media |

## 📸 Screenshots

### Main Interface
*Modern dark theme with intuitive file management*

### Tag Comparison
*Side-by-side comparison with change tracking*

### Settings Window
*Comprehensive configuration options*

### Online Database Search
*Intelligent matching with confidence scores*

## 🤝 Contributing

Contributions are welcome! Please feel free to submit pull requests or open issues for bugs and feature requests.

### Development Guidelines
- Follow C# coding standards
- Use nullable reference types
- Maintain MVVM architecture
- Include XML documentation for public APIs
- Test with multiple audio formats

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- **TagLibSharp** - Audio metadata processing
- **MusicBrainz** - Open music encyclopedia
- **Discogs** - Music database and marketplace
- **Microsoft** - .NET platform and WPF framework

## 📞 Support

- 🐛 **Bug Reports**: [Issues](../../issues)
- 💡 **Feature Requests**: [Discussions](../../discussions)
- 📖 **Documentation**: [Wiki](../../wiki)

---

<div align="center">

**Made with ❤️ for music lovers**

[⭐ Star this repo](../../stargazers) • [🍴 Fork it](../../fork) • [📢 Share it](https://twitter.com/intent/tweet?text=Check%20out%20TID3%20-%20Advanced%20ID3%20Tag%20Editor)

</div>
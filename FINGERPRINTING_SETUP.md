# Audio Fingerprinting Setup

To use the fingerprint recognition feature, you need to install Chromaprint (fpcalc.exe).

## Quick Setup

1. **Download Chromaprint**: 
   - Visit: https://acoustid.org/chromaprint
   - Download the Windows binary package
   - Or use direct link: https://github.com/acoustid/chromaprint/releases

2. **Extract and Place**:
   - Extract the downloaded archive
   - Copy `fpcalc.exe` to the `Tools/` folder in your TID3 directory
   - The build process automatically copies it to the output directory
   - Alternative locations (if not using Tools folder):
     - Same folder as TID3.exe
     - Add to your Windows PATH
     - Or place in `C:\Program Files\Chromaprint\`

3. **Get AcoustID API Key**:
   - Visit: https://acoustid.org/new-application
   - Register and create a new application
   - Copy the API key
   - Configure in TID3 Settings → API Configuration

## Testing

1. Open TID3 Settings
2. Enter your AcoustID API key
3. Click "Test AcoustID Connection" - should show success
4. Select an audio file and click the "Fingerprint" button
5. The best fingerprint match will be automatically applied to the tag comparison
6. Switch to the "Tag Comparison" tab to review the changes
7. Accept or reject individual changes as needed

## Features

- **Automatic Application**: The best fingerprint match is automatically applied
- **Complete Metadata**: Includes artist, title, and album information via MusicBrainz fallback
- **Tag Comparison**: Review all changes before saving
- **Multiple Results**: All fingerprint results are shown in the online sources dropdown

## Troubleshooting

- **"fpcalc.exe not found"**: Ensure fpcalc.exe is in the Tools folder or other search locations
- **Win32Exception**: May indicate missing Visual C++ Redistributables or architecture mismatch
- **Connection failed**: Check your AcoustID API key is valid
- **No results**: The audio file may not be in the AcoustID database
- **Use diagnostic feature**: Go to Settings → Test AcoustID Connection for detailed error information
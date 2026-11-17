# BadgeForge

BadgeForge is a Windows desktop app for designing photo ID badges and printing them in bulk to an
**IDP SMART-31** direct-to-card printer. Design a badge once, load your people from a spreadsheet,
match their photos, and print the whole batch.

It is written in C# on .NET 10, with [Avalonia](https://avaloniaui.net/) for the UI and
[SkiaSharp](https://github.com/mono/SkiaSharp) for 300 DPI card rendering.

## What it does

- **Badge designer.** Drag, resize and nudge layers on a live preview of the card. Layer types: text,
  photo, image, barcode / QR code and shape. Undo and redo, snapping, layer lock, and image adjustments
  (crop, brightness, contrast, saturation).
- **Card size and orientation.** CR80 and CR79, landscape or portrait, chosen in the app.
- **People list.** Add people one at a time, import a CSV or Excel (`.xlsx`) spreadsheet, or drop in photos
  and let the file names (for example `jane_smith.jpg`) fill in the names.
- **Photo matching.** Match photos to people from a folder.
- **Batch printing.** Start a run and let it go. The app pauses when the printer needs you (output hopper
  to empty, jam, out of cards or ribbon) and resumes when you answer. You can stop a run and reprint a
  single card.
- **Double-sided cards.** Design a front and a back. Needs a printer with a flipper.
- **PDF export.** Export the batch to a PDF instead of printing, for proofing.
- **Barcodes.** Code 128, Code 39, QR, EAN-13, UPC-A and PDF417.
- **Template library.** Save designs to *My Templates* or to any file and reopen them later.

## Requirements

| To run | To build from source |
| --- | --- |
| Windows 10 or 11 (64-bit; ARM64 build also available) | [.NET 10 SDK](https://dotnet.microsoft.com/download) |
| An IDP SMART-31 installed as a Windows printer, to print | Any OS can build and run the tests; printing needs Windows |

Without a printer you can still design badges, manage people and export PDFs. Printing is disabled and
the app tells you the printer isn't available.

## Get the app

### Build the executables

From the repo root on Windows:

```bat
publish-windows.cmd
```

or on Linux / macOS / Git Bash:

```bash
./publish-windows.sh
```

Add a runtime identifier for another target, for example `publish-windows.cmd win-arm64`. Output goes to
`publish/win-x64/`:

| File | Use |
| --- | --- |
| `portable.exe` (also `BadgeForge.exe`) | Self-contained single file. Copy it anywhere and double-click. No .NET install needed. |
| `installer.exe` (also `BadgeForge-Setup.exe`) | Installs BadgeForge with Start Menu and Desktop shortcuts. |

The executables are not code-signed. On first run Windows SmartScreen shows "unknown publisher": choose
**More info → Run anyway**. A copy that came over a network share or download may first need right-click →
**Properties → Unblock**.

### Run from source

```bash
dotnet run --project BadgeForge.App
```

## Using BadgeForge

1. **Pick the card.** Choose the card size and landscape or portrait. The preview resizes to match.
2. **Design the badge** on the *Design* page.
   - Start from the *Starter Employee ID Badge* (File menu) or a blank card.
   - Add layers with the **+** button, then drag them on the card or type exact values in the side panel.
   - Arrow keys nudge a layer 0.1 mm; hold Shift for 1 mm. Double-click text to edit it on the card.
   - Put data in a layer with a field marker such as `{{FirstName}} {{LastName}}`, `{{Department}}` or
     `{{EmployeeId}}` for a barcode. A photo layer uses `{{Photo}}`.
3. **Add people** on the *People* page: **Add person**, **Add from photos…**, or **Import spreadsheet…**
   (CSV or `.xlsx`). In the *Data* panel, map each spreadsheet column to a field on the card.
4. **Match photos.** Choose a photo folder in the *Data* panel, or use *Match photos from a folder…*.
5. **Check the preview.** Step through people with the record arrows. Until you add anyone, the card shows
   the field names (`{{FirstName}}`) instead of invented data.
6. **Print.** Choose your printer, tick the people to print and start the run. BadgeForge checks that the
   printer is there and takes this card size, that the pre-flight data check finds no errors, and
   that every barcode fits at a scannable size. Warnings are shown before anything is sent.
   Use **Export PDF…** to proof first.
7. **Save your design** with *Save* or *Save to My Templates…*. Templates in *My Templates* are stored in
   `%APPDATA%\BadgeForge\Templates`.

### During a print run

- The SMART-31 holds 80 cards in its input hopper and 25 in its output hopper. BadgeForge pauses every
  20 cards so you can empty the output hopper, then **Resume**.
- **Pause** holds the run; **Stop** lets you finish the card in the printer or eject it as it is. An ejected
  card marks that person for reprinting.
- Text and barcodes marked *Print as pure black (K resin)* are drawn without anti-aliasing so the printer
  puts them on the black resin panel.

## Project layout

| Project | Role |
| --- | --- |
| `BadgeForge.App` | Avalonia UI and view models |
| `BadgeForge.Core.Printing` | Printer driver, spooler, batch print engine |
| `BadgeForge.Core.Rendering` | SkiaSharp card renderer, barcodes, PDF export |
| `BadgeForge.Core.Templates` | Template and layer model, JSON storage |
| `BadgeForge.Core.Data` | CSV / Excel import, people, photo matching, validation |
| `BadgeForge.Installer` | Self-extracting setup program |
| `BadgeForge.Tests`, `BadgeForge.UiTests` | Unit and headless UI tests |

Printer capabilities are probed at runtime and checked against the card size. A mismatch fails the print
instead of stretching the card. [`BadgeForge-Project.md`](BadgeForge-Project.md) has the full specification
and design notes.

## Development

```bash
dotnet build BadgeForge.slnx
dotnet test BadgeForge.slnx
```

The tests use a stand-in printer driver (`BadgeForge.Tests/MockCardPrinterDriver.cs`) so they need no
hardware. The app itself ships no mock printer and no sample data.

`GoldenImageTests` compares a rendered card pixel for pixel against a reference image and currently fails
on Windows because of font rendering differences. It is a known issue, not a regression.

## Status and limits

- Printing has been exercised against the stand-in driver and the Windows spooler path. Verify K-resin
  routing on a real SMART-31 before relying on it for production runs.
- Routing through the printer driver's private DEVMODE flag is not implemented. BadgeForge refuses to
  print when that mode is selected, so a card can't come out with smudged text.

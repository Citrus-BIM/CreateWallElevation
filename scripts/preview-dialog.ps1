param(
    [ValidateSet('All', 'CreateWallElevation', 'CreateWallElevationSpectrum')]
    [string]$Variant = 'All',
    [string]$OutputDirectory = (Join-Path $env:TEMP 'CreateWallElevation-dialog-preview')
)

# Run with Windows PowerShell in STA mode. No Revit assemblies or visible window are used.
# powershell.exe -NoProfile -STA -File .\scripts\preview-dialog.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run this script using powershell.exe -NoProfile -STA -File.'
}
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$variants = if ($Variant -eq 'All') { @('CreateWallElevation', 'CreateWallElevationSpectrum') } else { @($Variant) }
$reports = New-Object 'System.Collections.Generic.List[object]'

function Assert-Preview([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-VisualElements([Windows.DependencyObject]$Element) {
    $Element
    $count = [Windows.Media.VisualTreeHelper]::GetChildrenCount($Element)
    for ($index = 0; $index -lt $count; $index++) {
        Get-VisualElements ([Windows.Media.VisualTreeHelper]::GetChild($Element, $index))
    }
}

foreach ($currentVariant in $variants) {
    $sourcePath = Join-Path $repositoryRoot "$currentVariant\CreateWallElevationWPF.xaml"
    [xml]$source = [IO.File]::ReadAllText($sourcePath)
    $xamlNamespace = 'http://schemas.microsoft.com/winfx/2006/xaml'
    $names = @($source.SelectNodes('//*[@*[local-name()="Name" and namespace-uri()="http://schemas.microsoft.com/winfx/2006/xaml"]]') |
        ForEach-Object { $_.GetAttribute('Name', $xamlNamespace) })
    $root = $source.DocumentElement
    $root.RemoveAttribute('Class', $xamlNamespace)
    $root.RemoveAttribute('Ignorable', 'http://schemas.openxmlformats.org/markup-compatibility/2006')
    # Event wiring is exercised by the normal compiled dialog; loose XAML has no code-behind.
    foreach ($node in $source.SelectNodes('//*')) {
        foreach ($eventName in @('KeyDown', 'Click', 'Checked', 'Unchecked')) { $node.RemoveAttribute($eventName) }
    }
    $root.RemoveAttribute('Icon')
    foreach ($dictionary in $source.SelectNodes('//*[local-name()="ResourceDictionary" and @Source]')) {
        $dictionary.SetAttribute('Source', ([Uri](Join-Path (Split-Path $sourcePath -Parent) $dictionary.Source)).AbsoluteUri)
    }
    $reader = New-Object Xml.XmlNodeReader $source
    $dialog = [Windows.Markup.XamlReader]::Load($reader)
    $reader.Close()

    $controls = @{}
    # Named template parts belong to template name scopes, so only collect dialog controls.
    foreach ($name in $names) {
        if ($name -in @('Outer', 'Inner')) { continue }
        $control = $dialog.FindName($name)
        Assert-Preview ($null -ne $control) "Missing named control: $currentVariant/$name"
        $controls[$name] = $control
    }
    foreach ($groupName in @('groupBox_BuildBy', 'groupBox_UseToBuild')) {
        $group = $controls[$groupName]
        Assert-Preview ($group -is [Windows.Controls.GroupBox]) "$groupName must stay a GroupBox."
        Assert-Preview ($group.Content -is [Windows.Controls.Grid]) "$groupName content must stay a Grid."
        $radioCount = @($group.Content.Children | Where-Object { $_ -is [Windows.Controls.RadioButton] }).Count
        Assert-Preview ($radioCount -eq 2) "$groupName must have two direct RadioButton children."
        Assert-Preview ([Windows.Input.KeyboardNavigation]::GetTabNavigation($group) -eq 'Once') "$groupName must be one tab stop."
        Assert-Preview ([Windows.Input.KeyboardNavigation]::GetDirectionalNavigation($group) -eq 'Cycle') "$groupName must cycle arrow navigation."
    }
    $tabOrder = @('groupBox_BuildBy', 'groupBox_UseToBuild', 'comboBox_SelectTypeSectionFacade',
        'checkBox_UseTemplate', 'comboBox_UseTemplate', 'textBox_Indent', 'textBox_ProjectionDepth',
        'textBox_IndentUp', 'textBox_IndentDown', 'textBox_CurveNumberOfSegments', 'textBox_MinSegmentLength',
        'textBox_ViewNamePrefix', 'comboBox_PlaceOnSheet', 'btn_Cancel', 'btn_Ok')
    $previousTabIndex = -1
    foreach ($controlName in $tabOrder) {
        $tabIndex = [Windows.Input.KeyboardNavigation]::GetTabIndex($controls[$controlName])
        Assert-Preview ($tabIndex -gt $previousTabIndex) "Tab order is incorrect at $controlName."
        $previousTabIndex = $tabIndex
    }
    Assert-Preview ([Windows.Input.KeyboardNavigation]::GetTabNavigation($dialog) -eq 'Cycle') 'Tab focus must remain in the dialog.'
    Assert-Preview ($controls.textBox_IndentUp.Text -eq '0' -and $controls.textBox_IndentDown.Text -eq '0') 'Vertical defaults must be zero.'
    Assert-Preview ($controls.comboBox_PlaceOnSheet.DisplayMemberPath -eq 'DisplayName') 'Sheet choices must use DisplayName.'
    Assert-Preview ($null -eq $controls.comboBox_PlaceOnSheet.ItemTemplate) 'Remove the legacy sheet ItemTemplate.'
    Assert-Preview ($controls.btn_Ok.IsDefault -and $controls.btn_Cancel.IsCancel) 'Default and cancel buttons must preserve Enter/Escape behavior.'

    # A detached content host renders the same WPF elements/resources at a conservative
    # client size, allowing 16x39 DIP for standard window chrome without showing a HWND.
    $clientWidth = $dialog.Width - 16
    $clientHeight = $dialog.Height - 39
    $content = $dialog.Content
    $dialog.Content = $null
    $hostBorder = New-Object Windows.Controls.Border
    [Windows.NameScope]::SetNameScope($hostBorder, [Windows.NameScope]::GetNameScope($dialog))
    $hostBorder.Resources = $dialog.Resources
    $hostBorder.Background = $dialog.Background
    $hostBorder.UseLayoutRounding = $true
    $hostBorder.SnapsToDevicePixels = $true
    $hostBorder.Language = [Windows.Markup.XmlLanguage]::GetLanguage('ru-RU')
    $hostBorder.SetValue([Windows.Controls.Control]::FontFamilyProperty, $dialog.FontFamily)
    $hostBorder.SetValue([Windows.Controls.Control]::FontSizeProperty, $dialog.FontSize)
    $hostBorder.SetValue([Windows.Controls.Control]::ForegroundProperty, $dialog.Foreground)
    $hostBorder.Child = $content

    $controls.comboBox_SelectTypeSectionFacade.ItemsSource = @('АИ_Развёртки')
    $controls.comboBox_SelectTypeSectionFacade.SelectedIndex = 0
    $controls.comboBox_UseTemplate.ItemsSource = @('АИ_18_Развёртки')
    $controls.comboBox_UseTemplate.SelectedIndex = 0
    $controls.comboBox_PlaceOnSheet.ItemsSource = @(
        [pscustomobject]@{ DisplayName = 'Без размещения на листе'; Sheet = $null },
        [pscustomobject]@{ DisplayName = '1 — Первый лист изменений'; Sheet = 'PreviewSheet' }
    )
    $controls.comboBox_PlaceOnSheet.SelectedIndex = 1

    $prefixScenarios = @(
        @{ Room = $true; Section = $true; Prefix = ''; Example = 'Р_П101_1_…' },
        @{ Room = $true; Section = $false; Prefix = ''; Example = 'Ф_П101_1_…' },
        @{ Room = $false; Section = $true; Prefix = ''; Example = 'Р_Ст_1_…' },
        @{ Room = $false; Section = $false; Prefix = ''; Example = 'Ф_Ст_1_…' },
        @{ Room = $true; Section = $true; Prefix = 'АР'; Example = 'АР_101_1_…' },
        @{ Room = $false; Section = $true; Prefix = 'АР'; Example = 'АР_1_…' }
    )
    foreach ($scenario in $prefixScenarios) {
        $controls.rbt_ByRoom.IsChecked = $scenario.Room
        $controls.rbt_ByWall.IsChecked = -not $scenario.Room
        $controls.rbt_Section.IsChecked = $scenario.Section
        $controls.rbt_Facade.IsChecked = -not $scenario.Section
        $controls.textBox_ViewNamePrefix.Text = $scenario.Prefix
        $hostBorder.Measure([Windows.Size]::new($clientWidth, $clientHeight))
        $hostBorder.Arrange([Windows.Rect]::new(0, 0, $clientWidth, $clientHeight))
        $hostBorder.UpdateLayout()
        $hostBorder.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ApplicationIdle)
        $prefixMatches = @(Get-VisualElements $hostBorder | Where-Object {
            $_ -is [Windows.Controls.TextBlock] -and $_.Text -eq $scenario.Example
        })
        Assert-Preview ($prefixMatches.Count -gt 0) "Prefix scenario failed: $($scenario.Example)"
    }

    foreach ($state in @('room-facade', 'wall-section')) {
        $isRoom = $state -eq 'room-facade'
        $controls.rbt_ByRoom.IsChecked = $isRoom
        $controls.rbt_ByWall.IsChecked = -not $isRoom
        $controls.rbt_Facade.IsChecked = $isRoom
        $controls.rbt_Section.IsChecked = -not $isRoom
        $controls.checkBox_UseTemplate.IsChecked = $isRoom
        $controls.comboBox_UseTemplate.IsEnabled = $isRoom
        $controls.groupBox_Simplify.IsEnabled = $isRoom
        $controls.textBox_Indent.Text = '300'
        $controls.textBox_ViewNamePrefix.Text = if ($isRoom) { 'АР' } else { '' }
        $controls.comboBox_PlaceOnSheet.SelectedIndex = if ($isRoom) { 1 } else { 0 }
        $hostBorder.Measure([Windows.Size]::new($clientWidth, $clientHeight))
        $hostBorder.Arrange([Windows.Rect]::new(0, 0, $clientWidth, $clientHeight))
        $hostBorder.UpdateLayout()
        $hostBorder.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ApplicationIdle)

        $bounds = @()
        foreach ($entry in $controls.GetEnumerator() | Sort-Object Name) {
            $control = $entry.Value
            if ($control.Visibility -ne [Windows.Visibility]::Visible) { continue }
            $point = $control.TranslatePoint([Windows.Point]::new(0, 0), $hostBorder)
            $right = $point.X + $control.ActualWidth
            $bottom = $point.Y + $control.ActualHeight
            Assert-Preview ($point.X -ge -0.5 -and $point.Y -ge -0.5 -and $right -le $clientWidth + 0.5 -and $bottom -le $clientHeight + 0.5) "Control overflows: $currentVariant/$state/$($entry.Name)"
            if ($control -is [Windows.Controls.TextBox] -or $control -is [Windows.Controls.ComboBox]) {
                Assert-Preview ([Math]::Abs($control.ActualHeight - 32) -lt 0.1) "Input height must be 32 DIP: $($entry.Name)"
            }
            $bounds += [pscustomobject]@{
                name = $entry.Name; x = $point.X; y = $point.Y
                width = $control.ActualWidth; height = $control.ActualHeight
                enabled = $control.IsEnabled; tabIndex = [Windows.Input.KeyboardNavigation]::GetTabIndex($control)
            }
        }
        $sheetBounds = $bounds | Where-Object name -eq 'comboBox_PlaceOnSheet'
        Assert-Preview ($sheetBounds.y + $sheetBounds.height -le $clientHeight - 64) 'Body content overlaps the footer.'
        Assert-Preview ($controls.textBox_MinSegmentLength.IsEnabled -eq $isRoom) 'Minimum length does not follow build mode.'
        foreach ($name in @('textBox_Indent', 'textBox_IndentUp', 'textBox_IndentDown', 'textBox_ProjectionDepth', 'textBox_CurveNumberOfSegments')) {
            Assert-Preview $controls[$name].IsEnabled "Wall mode must not disable $name."
        }
        Assert-Preview ($controls.comboBox_UseTemplate.IsEnabled -eq $isRoom) 'Template enabled state is incorrect.'
        Assert-Preview ($controls.comboBox_UseTemplate.SelectedItem -eq 'АИ_18_Развёртки') 'Disabled template must preserve its visible selection.'
        $visualElements = @(Get-VisualElements $hostBorder)
        $prefixText = if ($isRoom) { 'АР_101_1_…' } else { 'Р_Ст_1_…' }
        Assert-Preview (@($visualElements | Where-Object { $_ -is [Windows.Controls.TextBlock] -and $_.Text -eq $prefixText }).Count -gt 0) "Prefix example did not update: $prefixText"
        foreach ($radioName in @('rbt_ByRoom', 'rbt_ByWall', 'rbt_Section', 'rbt_Facade')) {
            $radio = $controls[$radioName]
            $indicator = $radio.Template.FindName('Inner', $radio)
            Assert-Preview (($indicator.Visibility -eq [Windows.Visibility]::Visible) -eq ($radio.IsChecked -eq $true)) "Radio indicator is incorrect: $radioName"
        }

        # Render at 150% for legible inspection while preserving layout measured in DIP.
        $scale = 1.5
        $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]($clientWidth * $scale)), ([int]($clientHeight * $scale)), 144, 144, ([Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($hostBorder)
        $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $imagePath = Join-Path $OutputDirectory "$currentVariant-$state.png"
        $stream = [IO.File]::Create($imagePath)
        try { $encoder.Save($stream) } finally { $stream.Dispose() }
        $reports.Add([pscustomobject]@{
            variant = $currentVariant; state = $state; image = $imagePath
            windowDip = @($dialog.Width, $dialog.Height); clientDip = @($clientWidth, $clientHeight)
            fontSizeDip = $dialog.FontSize; namedControlsVerified = $controls.Count
            overflow = $false; inputHeightDip = 32; defaultAndCancel = $true
            radioGroupContract = $true; templateSelectionRetained = $true; tabOrderConfiguration = $tabOrder
            minimumLengthEnabled = $controls.textBox_MinSegmentLength.IsEnabled
            remainingGeometryEnabled = $true; prefixExample = $prefixText
            prefixModesVerified = $prefixScenarios.Count; controls = $bounds
        })
    }
    $dialog.Close()
}
$reportPath = Join-Path $OutputDirectory 'dialog-preview-report.json'
$reports | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$reports | Select-Object variant, state, image, overflow, namedControlsVerified, minimumLengthEnabled | Format-Table -AutoSize
Write-Output "Report: $reportPath"

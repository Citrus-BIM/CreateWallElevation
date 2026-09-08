using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Grid = System.Windows.Controls.Grid;

namespace CreateWallElevation
{
    public partial class CreateWallElevationWPF : Window
    {
        private readonly Document Doc;
        private readonly List<ViewSheet> _viewSheetList;

        private List<ViewFamilyType> ViewFamilyTypeList = new List<ViewFamilyType>();
        private List<ViewSection> ViewSectionTemplateList = new List<ViewSection>();

        public ViewFamilyType SelectedViewFamilyType;
        public bool UseTemplate;
        public ViewSection ViewSectionTemplate;
        public string SelectedBuildByName;
        public string SelectedUseToBuildName;
        public double Indent;
        public double IndentUp;
        public double IndentDown;
        public string ViewNamePrefix;
        public double ProjectionDepth;
        public int CurveNumberOfSegments;
        public ViewSheet SelectedViewSheet;

        public double MinSegmentLength; // internal units (ft)

        private CreateWallElevationSettings CreateWallElevationSettingsItem;

        // флаг: UI полностью готов (после Loaded)
        private bool _uiReady;

        public CreateWallElevationWPF(Document doc, List<ViewSheet> viewSheetList)
        {
            Doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _viewSheetList = viewSheetList ?? new List<ViewSheet>();

            CreateWallElevationSettingsItem = CreateWallElevationSettings.GetSettings();

            InitializeComponent();

            // ВАЖНО: ничего не трогаем, что может вызвать Checked-события/работу с x:Name,
            // пока окно не загрузилось полностью.
            Loaded += CreateWallElevationWPF_Loaded;
        }

        private void CreateWallElevationWPF_Loaded(object sender, RoutedEventArgs e)
        {
            if (_uiReady) return;
            _uiReady = true;

            // Лист
            if (comboBox_PlaceOnSheet != null)
                comboBox_PlaceOnSheet.ItemsSource = _viewSheetList;

            // Радио-кнопки из настроек
            if (CreateWallElevationSettingsItem != null)
            {
                if (rbt_ByRoom != null)
                    rbt_ByRoom.IsChecked = CreateWallElevationSettingsItem.SelectedBuildByName == "rbt_ByRoom";
                if (rbt_ByWall != null && rbt_ByRoom != null)
                    rbt_ByWall.IsChecked = !(rbt_ByRoom.IsChecked == true);

                if (rbt_Section != null)
                    rbt_Section.IsChecked = CreateWallElevationSettingsItem.SelectedUseToBuildName == "rbt_Section";
                if (rbt_Facade != null && rbt_Section != null)
                    rbt_Facade.IsChecked = !(rbt_Section.IsChecked == true);
            }

            // Типы видов (Section/Elevation) — наполняем после выставления радио
            RefreshViewFamilyTypes();

            // Восстановить выбранный ViewFamilyType
            if (CreateWallElevationSettingsItem != null && comboBox_SelectTypeSectionFacade != null && ViewFamilyTypeList.Count != 0)
            {
                var savedVft = ViewFamilyTypeList.FirstOrDefault(vft => vft.Name == CreateWallElevationSettingsItem.SelectedViewFamilyTypeName);
                comboBox_SelectTypeSectionFacade.SelectedItem = savedVft ?? comboBox_SelectTypeSectionFacade.Items[0];
            }
            else if (comboBox_SelectTypeSectionFacade != null && comboBox_SelectTypeSectionFacade.Items.Count > 0 && comboBox_SelectTypeSectionFacade.SelectedItem == null)
            {
                comboBox_SelectTypeSectionFacade.SelectedItem = comboBox_SelectTypeSectionFacade.Items[0];
            }

            // Тексты полей
            if (CreateWallElevationSettingsItem != null)
            {
                if (textBox_Indent != null) textBox_Indent.Text = CreateWallElevationSettingsItem.Indent;
                if (textBox_IndentUp != null) textBox_IndentUp.Text = CreateWallElevationSettingsItem.IndentUp;
                if (textBox_IndentDown != null) textBox_IndentDown.Text = CreateWallElevationSettingsItem.IndentDown;
                if (textBox_ViewNamePrefix != null) textBox_ViewNamePrefix.Text = CreateWallElevationSettingsItem.ViewNamePrefix ?? "";
                if (textBox_ProjectionDepth != null) textBox_ProjectionDepth.Text = CreateWallElevationSettingsItem.ProjectionDepth;
                if (textBox_CurveNumberOfSegments != null) textBox_CurveNumberOfSegments.Text = CreateWallElevationSettingsItem.CurveNumberOfSegments;

                if (textBox_MinSegmentLength != null)
                {
                    textBox_MinSegmentLength.Text = string.IsNullOrWhiteSpace(CreateWallElevationSettingsItem.MinSegmentLength)
                        ? "1000"
                        : CreateWallElevationSettingsItem.MinSegmentLength;
                }

                // Лист (выбранный)
                if (_viewSheetList.Count != 0 && comboBox_PlaceOnSheet != null)
                {
                    ViewSheet savedSheet = null;
                    if (!string.IsNullOrWhiteSpace(CreateWallElevationSettingsItem.SelectedViewSheetNumber))
                    {
                        savedSheet = _viewSheetList.FirstOrDefault(vs =>
                            vs.SheetNumber == CreateWallElevationSettingsItem.SelectedViewSheetNumber);
                    }
                    if (savedSheet == null && !string.IsNullOrWhiteSpace(CreateWallElevationSettingsItem.SelectedViewSheetName))
                    {
                        savedSheet = _viewSheetList.FirstOrDefault(vs =>
                            vs.Name == CreateWallElevationSettingsItem.SelectedViewSheetName);
                    }
                    comboBox_PlaceOnSheet.SelectedItem = savedSheet ?? comboBox_PlaceOnSheet.Items[0];
                }

                // Шаблон
                if (checkBox_UseTemplate != null)
                {
                    checkBox_UseTemplate.IsChecked = CreateWallElevationSettingsItem.UseTemplate;
                    RefreshTemplateList();

                    if (checkBox_UseTemplate.IsChecked == true && comboBox_UseTemplate != null && ViewSectionTemplateList.Count != 0)
                    {
                        var savedTpl = ViewSectionTemplateList.FirstOrDefault(vs => vs.Name == CreateWallElevationSettingsItem.ViewSectionTemplateName);
                        comboBox_UseTemplate.SelectedItem = savedTpl ?? comboBox_UseTemplate.Items[0];
                    }
                }
            }
            else
            {
                if (_viewSheetList.Count != 0 && comboBox_PlaceOnSheet != null)
                    comboBox_PlaceOnSheet.SelectedItem = comboBox_PlaceOnSheet.Items[0];

                if (textBox_MinSegmentLength != null)
                    textBox_MinSegmentLength.Text = "1000";
            }

            UpdateSimplifyUi();
        }

        // ---------------- UI events ----------------

        private void btn_Ok_Click(object sender, RoutedEventArgs e)
        {
            TryAccept();
        }

        private void btn_Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CreateWallElevationWPF_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBoxBase || Keyboard.FocusedElement is ComboBox)
                return;

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                TryAccept();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void BuildByCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;
            UpdateSimplifyUi();
        }

        private void UseToBuildCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;
            RefreshViewFamilyTypes();
            RefreshTemplateList();
        }

        private void checkBox_UseTemplate_Checked(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;
            RefreshTemplateList();
        }

        // ---------------- helpers ----------------

        private void UpdateSimplifyUi()
        {
            if (groupBox_Simplify == null) return;
            groupBox_Simplify.IsEnabled = (rbt_ByRoom != null && rbt_ByRoom.IsChecked == true);
        }

        private void RefreshViewFamilyTypes()
        {
            if (comboBox_SelectTypeSectionFacade == null || groupBox_UseToBuild == null) return;

            string useToBuildSelectedName = GetCheckedRadioName(groupBox_UseToBuild, "rbt_Section");

            if (useToBuildSelectedName == "rbt_Section")
            {
                ViewFamilyTypeList = new FilteredElementCollector(Doc)
                    .OfClass(typeof(ViewFamilyType))
                    .WhereElementIsElementType()
                    .Cast<ViewFamilyType>()
                    .Where(vft => vft.ViewFamily == ViewFamily.Section)
                    .OrderBy(vft => vft.Name, new AlphanumComparatorFastString())
                    .ToList();
            }
            else
            {
                ViewFamilyTypeList = new FilteredElementCollector(Doc)
                    .OfClass(typeof(ViewFamilyType))
                    .WhereElementIsElementType()
                    .Cast<ViewFamilyType>()
                    .Where(vft => vft.ViewFamily == ViewFamily.Elevation)
                    .OrderBy(vft => vft.Name, new AlphanumComparatorFastString())
                    .ToList();
            }

            comboBox_SelectTypeSectionFacade.ItemsSource = ViewFamilyTypeList;
            comboBox_SelectTypeSectionFacade.DisplayMemberPath = "Name";

            if (comboBox_SelectTypeSectionFacade.Items.Count > 0 && comboBox_SelectTypeSectionFacade.SelectedItem == null)
                comboBox_SelectTypeSectionFacade.SelectedItem = comboBox_SelectTypeSectionFacade.Items[0];
        }

        private void RefreshTemplateList()
        {
            if (checkBox_UseTemplate == null || comboBox_UseTemplate == null) return;

            if (checkBox_UseTemplate.IsChecked == true)
            {
                comboBox_UseTemplate.IsEnabled = true;

                ViewSectionTemplateList = new FilteredElementCollector(Doc)
                    .OfClass(typeof(ViewSection))
                    .Cast<ViewSection>()
                    .Where(vs => vs.IsTemplate)
                    .OrderBy(vs => vs.Name, new AlphanumComparatorFastString())
                    .ToList();

                comboBox_UseTemplate.ItemsSource = ViewSectionTemplateList;
                comboBox_UseTemplate.DisplayMemberPath = "Name";

                if (comboBox_UseTemplate.Items.Count > 0 && comboBox_UseTemplate.SelectedItem == null)
                    comboBox_UseTemplate.SelectedItem = comboBox_UseTemplate.Items[0];
            }
            else
            {
                comboBox_UseTemplate.IsEnabled = false;
            }
        }

        private static string GetCheckedRadioName(GroupBox groupBox, string fallbackName)
        {
            var grid = groupBox.Content as Grid;
            if (grid == null) return fallbackName;

            var rb = grid.Children
                .OfType<RadioButton>()
                .FirstOrDefault(x => x.IsChecked == true);

            return rb != null ? rb.Name : fallbackName;
        }

        private void TryAccept()
        {
            try
            {
                CaptureSettings();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "Развёртки стен", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try { CreateWallElevationSettingsItem.SaveSettings(); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                MessageBox.Show(this, "Параметры будут использованы в этом запуске, но сохранить их не удалось: " + ex.Message,
                    "Развёртки стен", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            DialogResult = true;
            Close();
        }

        private void CaptureSettings()
        {
            SelectedBuildByName = GetCheckedRadioName(groupBox_BuildBy, "rbt_ByRoom");
            SelectedUseToBuildName = GetCheckedRadioName(groupBox_UseToBuild, "rbt_Section");
            SelectedViewFamilyType = comboBox_SelectTypeSectionFacade.SelectedItem as ViewFamilyType;
            if (SelectedViewFamilyType == null)
                throw new ArgumentException("Выберите существующий тип разреза или фасада.");
            if (SelectedUseToBuildName != "rbt_Section" && (!(Doc.ActiveView is ViewPlan) || Doc.ActiveView.IsTemplate))
                throw new ArgumentException("Для построения фасадов откройте план этажа.");

            Indent = RevitUnits.FromMillimeters(InputValues.Millimeters(textBox_Indent.Text, "Отступ от грани", false));
            IndentUp = RevitUnits.FromMillimeters(InputValues.Millimeters(textBox_IndentUp.Text, "Отступ сверху", false));
            IndentDown = RevitUnits.FromMillimeters(InputValues.SignedMillimeters(textBox_IndentDown.Text, "Смещение снизу"));
            ViewNamePrefix = ViewNaming.NormalizePrefix(textBox_ViewNamePrefix.Text);
            ProjectionDepth = RevitUnits.FromMillimeters(InputValues.Millimeters(textBox_ProjectionDepth.Text, "Глубина проекции", true));
            MinSegmentLength = SelectedBuildByName == "rbt_ByRoom"
                ? RevitUnits.FromMillimeters(InputValues.Millimeters(textBox_MinSegmentLength.Text, "Мин. длина сегмента", true))
                : RevitUnits.FromMillimeters(1000);
            CurveNumberOfSegments = InputValues.Segments(textBox_CurveNumberOfSegments.Text);
            if (ProjectionDepth <= Indent)
                throw new ArgumentException("Глубина проекции должна быть больше отступа от грани, чтобы стена попадала в вид.");

            UseTemplate = checkBox_UseTemplate.IsChecked == true;
            ViewSectionTemplate = UseTemplate ? comboBox_UseTemplate.SelectedItem as ViewSection : null;
            if (UseTemplate && ViewSectionTemplate == null)
                throw new ArgumentException("Выберите шаблон для выбранного типа вида или отключите использование шаблона.");
            SelectedViewSheet = comboBox_PlaceOnSheet.SelectedItem as ViewSheet;
            CreateWallElevationSettingsItem = new CreateWallElevationSettings
            {
                SelectedBuildByName = SelectedBuildByName,
                SelectedUseToBuildName = SelectedUseToBuildName,
                SelectedViewFamilyTypeName = SelectedViewFamilyType.Name,
                Indent = textBox_Indent.Text.Trim(),
                IndentUp = textBox_IndentUp.Text.Trim(),
                IndentDown = textBox_IndentDown.Text.Trim(),
                UsesSignedBottomOffset = true,
                ViewNamePrefix = ViewNamePrefix,
                ProjectionDepth = textBox_ProjectionDepth.Text.Trim(),
                MinSegmentLength = textBox_MinSegmentLength.Text.Trim(),
                CurveNumberOfSegments = CurveNumberOfSegments.ToString(System.Globalization.CultureInfo.InvariantCulture),
                UseTemplate = UseTemplate,
                ViewSectionTemplateName = ViewSectionTemplate?.Name,
                SelectedViewSheetNumber = SelectedViewSheet?.SheetNumber,
                SelectedViewSheetName = SelectedViewSheet?.Name
            };
        }
    }
}

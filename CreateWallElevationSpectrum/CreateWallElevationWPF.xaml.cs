using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        // 1) Переписанный CreateWallElevationWPF_Loaded целиком
        private void CreateWallElevationWPF_Loaded(object sender, RoutedEventArgs e)
        {
            if (_uiReady) return;
            _uiReady = true;

            // Лист
            if (comboBox_PlaceOnSheet != null)
                comboBox_PlaceOnSheet.ItemsSource = _viewSheetList;

            // Настройки (в "потолочной" схеме всегда non-null)
            var s = CreateWallElevationSettingsItem ?? new CreateWallElevationSettings();

            // --- Радио-кнопки (пусто -> дефолт) ---
            if (rbt_ByRoom != null && rbt_ByWall != null)
            {
                var byRoom = string.IsNullOrWhiteSpace(s.SelectedBuildByName) || s.SelectedBuildByName == "rbt_ByRoom";
                rbt_ByRoom.IsChecked = byRoom;
                rbt_ByWall.IsChecked = !byRoom;
            }

            if (rbt_Section != null && rbt_Facade != null)
            {
                var section = string.IsNullOrWhiteSpace(s.SelectedUseToBuildName) || s.SelectedUseToBuildName == "rbt_Section";
                rbt_Section.IsChecked = section;
                rbt_Facade.IsChecked = !section;
            }

            // --- Типы видов (Section/Elevation) — после радио ---
            RefreshViewFamilyTypes();

            // --- Восстановить выбранный ViewFamilyType ---
            if (comboBox_SelectTypeSectionFacade != null && ViewFamilyTypeList.Count > 0)
            {
                var savedVft = ViewFamilyTypeList.FirstOrDefault(vft => vft.Name == s.SelectedViewFamilyTypeName);
                comboBox_SelectTypeSectionFacade.SelectedItem = savedVft ?? ViewFamilyTypeList[0];
            }
            else if (comboBox_SelectTypeSectionFacade != null &&
                     comboBox_SelectTypeSectionFacade.Items.Count > 0 &&
                     comboBox_SelectTypeSectionFacade.SelectedItem == null)
            {
                comboBox_SelectTypeSectionFacade.SelectedItem = comboBox_SelectTypeSectionFacade.Items[0];
            }

            // --- Тексты полей (пусто -> дефолт) ---
            if (textBox_Indent != null) textBox_Indent.Text = string.IsNullOrWhiteSpace(s.Indent) ? "0" : s.Indent;
            if (textBox_IndentUp != null) textBox_IndentUp.Text = string.IsNullOrWhiteSpace(s.IndentUp) ? "0" : s.IndentUp;
            if (textBox_IndentDown != null) textBox_IndentDown.Text = string.IsNullOrWhiteSpace(s.IndentDown) ? "0" : s.IndentDown;
            if (textBox_ProjectionDepth != null) textBox_ProjectionDepth.Text = string.IsNullOrWhiteSpace(s.ProjectionDepth) ? "0" : s.ProjectionDepth;
            if (textBox_CurveNumberOfSegments != null) textBox_CurveNumberOfSegments.Text = string.IsNullOrWhiteSpace(s.CurveNumberOfSegments) ? "5" : s.CurveNumberOfSegments;

            if (textBox_MinSegmentLength != null)
                textBox_MinSegmentLength.Text = string.IsNullOrWhiteSpace(s.MinSegmentLength) ? "1000" : s.MinSegmentLength;

            // --- Лист (выбранный) ---
            if (_viewSheetList.Count > 0 && comboBox_PlaceOnSheet != null)
            {
                var savedSheet = _viewSheetList.FirstOrDefault(vs => vs.Name == s.SelectedViewSheetName);
                comboBox_PlaceOnSheet.SelectedItem = savedSheet ?? _viewSheetList[0];
            }

            // --- Шаблон ---
            if (checkBox_UseTemplate != null)
            {
                checkBox_UseTemplate.IsChecked = s.UseTemplate;
                RefreshTemplateList();

                if (checkBox_UseTemplate.IsChecked == true && comboBox_UseTemplate != null && ViewSectionTemplateList.Count > 0)
                {
                    var savedTpl = ViewSectionTemplateList.FirstOrDefault(vs => vs.Name == s.ViewSectionTemplateName);
                    comboBox_UseTemplate.SelectedItem = savedTpl ?? ViewSectionTemplateList[0];
                }
            }

            UpdateSimplifyUi();
        }

        // ---------------- UI events ----------------

        private void btn_Ok_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            DialogResult = true;
            Close();
        }

        private void btn_Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CreateWallElevationWPF_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                SaveSettings();
                DialogResult = true;
                Close();
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

        private static double ParseMmOrZero(string text)
        {
            text = (text ?? "").Trim();

            // 1) Текущая культура (обычно корректно для пользователя)
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v))
                return v;

            // 2) Fallback: нормализуем десятичный разделитель и парсим инвариантно
            var normalized = text.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;

            return 0.0;
        }

        // ---------------- settings ----------------
        private void SaveSettings()
        {
            CreateWallElevationSettingsItem = CreateWallElevationSettingsItem ?? new CreateWallElevationSettings();

            string NormOrDefault(string text, string def)
            {
                var t = (text ?? "").Trim();
                return string.IsNullOrWhiteSpace(t) ? def : t;
            }

            // ViewFamilyType
            SelectedViewFamilyType = comboBox_SelectTypeSectionFacade != null
                ? comboBox_SelectTypeSectionFacade.SelectedItem as ViewFamilyType
                : null;

            CreateWallElevationSettingsItem.SelectedViewFamilyTypeName =
                SelectedViewFamilyType != null ? SelectedViewFamilyType.Name : null;

            // Радио
            SelectedBuildByName = GetCheckedRadioName(groupBox_BuildBy, "rbt_ByRoom");
            CreateWallElevationSettingsItem.SelectedBuildByName = SelectedBuildByName;

            SelectedUseToBuildName = GetCheckedRadioName(groupBox_UseToBuild, "rbt_Section");
            CreateWallElevationSettingsItem.SelectedUseToBuildName = SelectedUseToBuildName;

            // Парсинг мм (культура/запятая/точка) — для расчётов
            var indentMm = ParseMmOrZero(textBox_Indent != null ? textBox_Indent.Text : null);
            var indentUpMm = ParseMmOrZero(textBox_IndentUp != null ? textBox_IndentUp.Text : null);
            var indentDownMm = ParseMmOrZero(textBox_IndentDown != null ? textBox_IndentDown.Text : null);
            var projMm = ParseMmOrZero(textBox_ProjectionDepth != null ? textBox_ProjectionDepth.Text : null);
            var minSegMm = ParseMmOrZero(textBox_MinSegmentLength != null ? textBox_MinSegmentLength.Text : null);

#if R2019 || R2020 || R2021
            Indent = UnitUtils.ConvertToInternalUnits(indentMm, DisplayUnitType.DUT_MILLIMETERS);
            IndentUp = UnitUtils.ConvertToInternalUnits(indentUpMm, DisplayUnitType.DUT_MILLIMETERS);
            IndentDown = UnitUtils.ConvertToInternalUnits(indentDownMm, DisplayUnitType.DUT_MILLIMETERS);
            ProjectionDepth = UnitUtils.ConvertToInternalUnits(projMm, DisplayUnitType.DUT_MILLIMETERS);
            MinSegmentLength = UnitUtils.ConvertToInternalUnits(minSegMm, DisplayUnitType.DUT_MILLIMETERS);
#else
            Indent = UnitUtils.ConvertToInternalUnits(indentMm, UnitTypeId.Millimeters);
            IndentUp = UnitUtils.ConvertToInternalUnits(indentUpMm, UnitTypeId.Millimeters);
            IndentDown = UnitUtils.ConvertToInternalUnits(indentDownMm, UnitTypeId.Millimeters);
            ProjectionDepth = UnitUtils.ConvertToInternalUnits(projMm, UnitTypeId.Millimeters);
            MinSegmentLength = UnitUtils.ConvertToInternalUnits(minSegMm, UnitTypeId.Millimeters);
#endif

            // Сохраняем строки (trim + пусто -> дефолт) — чтобы XML был консистентный
            CreateWallElevationSettingsItem.Indent = NormOrDefault(textBox_Indent != null ? textBox_Indent.Text : null, "0");
            CreateWallElevationSettingsItem.IndentUp = NormOrDefault(textBox_IndentUp != null ? textBox_IndentUp.Text : null, "0");
            CreateWallElevationSettingsItem.IndentDown = NormOrDefault(textBox_IndentDown != null ? textBox_IndentDown.Text : null, "0");
            CreateWallElevationSettingsItem.ProjectionDepth = NormOrDefault(textBox_ProjectionDepth != null ? textBox_ProjectionDepth.Text : null, "0");
            CreateWallElevationSettingsItem.MinSegmentLength = NormOrDefault(textBox_MinSegmentLength != null ? textBox_MinSegmentLength.Text : null, "1000");

            // Template
            UseTemplate = (checkBox_UseTemplate != null && checkBox_UseTemplate.IsChecked == true);
            CreateWallElevationSettingsItem.UseTemplate = UseTemplate;

            if (UseTemplate)
            {
                ViewSectionTemplate = comboBox_UseTemplate != null ? comboBox_UseTemplate.SelectedItem as ViewSection : null;
                CreateWallElevationSettingsItem.ViewSectionTemplateName = ViewSectionTemplate != null ? ViewSectionTemplate.Name : null;
            }
            else
            {
                CreateWallElevationSettingsItem.ViewSectionTemplateName = null;
            }

            // Curve segments (int + строка с дефолтом)
            int.TryParse((textBox_CurveNumberOfSegments != null ? textBox_CurveNumberOfSegments.Text : null),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out CurveNumberOfSegments);

            CreateWallElevationSettingsItem.CurveNumberOfSegments =
                NormOrDefault(textBox_CurveNumberOfSegments != null ? textBox_CurveNumberOfSegments.Text : null, "5");

            // Sheet
            SelectedViewSheet = comboBox_PlaceOnSheet != null ? comboBox_PlaceOnSheet.SelectedItem as ViewSheet : null;
            CreateWallElevationSettingsItem.SelectedViewSheetName = SelectedViewSheet != null ? SelectedViewSheet.Name : null;

            // Persist
            CreateWallElevationSettingsItem.SaveSettings();
        }
    }
}

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace CreateWallElevation
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class CreateWallElevationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            _ = RecordUsageSafely();
            UIDocument uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument == null || uiDocument.Document.IsFamilyDocument)
            {
                TaskDialog.Show("Развёртки стен", "Откройте документ проекта Revit.");
                return Result.Cancelled;
            }
            try
            {
                Document doc = uiDocument.Document;
                var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber, new AlphanumComparatorFastString()).ToList();
                var window = new CreateWallElevationWPF(doc, sheets);
                if (window.ShowDialog() != true) return Result.Cancelled;
                return new WallElevationBuilder(doc, window).Run(uiDocument);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = "Не удалось построить развёртки: " + ex.Message;
                return Result.Failed;
            }
        }

        private static async Task RecordUsageSafely()
        {
            try { await GetPluginStartInfo(); }
            catch { /* Optional usage reporting must not prevent the command from running. */ }
        }
        private static async Task GetPluginStartInfo()
        {
            Assembly thisAssembly = Assembly.GetExecutingAssembly();
            string assemblyName = "CreateWallElevation";
            string assemblyNameRus = "Развертки стен";
            string assemblyFolderPath = Path.GetDirectoryName(thisAssembly.Location);

            int lastBackslashIndex = assemblyFolderPath.LastIndexOf("\\");
            string dllPath = assemblyFolderPath.Substring(0, lastBackslashIndex + 1) + "PluginInfoCollector\\PluginInfoCollector.dll";

            Assembly assembly = Assembly.LoadFrom(dllPath);
            Type type = assembly.GetType("PluginInfoCollector.InfoCollector");

            if (type != null)
            {
                object instance = Activator.CreateInstance(type);
                var method = type.GetMethod("CollectPluginUsageAsync");

                if (method != null)
                {
                    Task task = (Task)method.Invoke(instance, new object[] { assemblyName, assemblyNameRus });
                    await task;
                }
            }
        }
    }
}

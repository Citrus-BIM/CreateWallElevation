using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CreateWallElevation
{
    internal static class SheetLayout
    {
        public static bool Place(Document doc, ViewSheet sheet, ViewSection view, out string reason)
        {
            reason = null;
            if (sheet.IsPlaceholder || !Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
            {
                reason = "вид нельзя разместить на выбранном листе";
                return false;
            }
            try
            {
                var titleBlocks = new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType().ToElements();
                if (titleBlocks.Count == 0)
                {
                    reason = "у листа нет основной надписи: невозможно определить область размещения";
                    return false;
                }
                BoundingBoxXYZ paper = titleBlocks[0].get_BoundingBox(sheet);
                if (paper == null) { reason = "не удалось определить размер листа"; return false; }
                // Reserve the lower strip for the title block and leave paper margins.
                var bounds = new Rect2(paper.Min.X + RevitUnits.FromMillimeters(20), paper.Min.Y + RevitUnits.FromMillimeters(60),
                    paper.Max.X - RevitUnits.FromMillimeters(15), paper.Max.Y - RevitUnits.FromMillimeters(20));
                var occupied = new List<Rect2>();
                foreach (ElementId id in sheet.GetAllViewports())
                    occupied.Add(ViewportBounds((Viewport)doc.GetElement(id)));
                foreach (Element element in new FilteredElementCollector(doc, sheet.Id).WhereElementIsNotElementType())
                {
                    if (!element.ViewSpecific || element is Viewport || titleBlocks.Any(t => t.Id.Equals(element.Id))) continue;
                    BoundingBoxXYZ box = element.get_BoundingBox(sheet);
                    if (box != null && box.Max.X > box.Min.X && box.Max.Y > box.Min.Y)
                        occupied.Add(new Rect2(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y));
                }
                using (var transaction = new SubTransaction(doc))
                {
                    transaction.Start();
                    Viewport viewport = Viewport.Create(doc, sheet.Id, view.Id, XYZ.Zero);
                    doc.Regenerate();
                    Rect2 actual = ViewportBounds(viewport);
                    if (!Geometry2D.TryPlace(actual.MaxX - actual.MinX, actual.MaxY - actual.MinY, bounds, occupied,
                        RevitUnits.FromMillimeters(10), out Rect2 target))
                    {
                        transaction.RollBack();
                        reason = "на листе нет свободного места; вид оставлен без размещения";
                        return false;
                    }
                    ElementTransformUtils.MoveElement(doc, viewport.Id, new XYZ(target.MinX - actual.MinX, target.MinY - actual.MinY, 0));
                    doc.Regenerate();
                    Rect2 final = ViewportBounds(viewport);
                    if (!bounds.Contains(final) || occupied.Any(r => final.Overlaps(r, RevitUnits.FromMillimeters(9.9))))
                    {
                        transaction.RollBack();
                        reason = "фактический габарит вида пересекает занятое место или границу листа";
                        return false;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                    { reason = "Revit отменил размещение видового экрана"; return false; }
                }
                return true;
            }
            catch (Exception ex) when (WallElevationBuilder.IsItemError(ex))
            {
                reason = "не удалось разместить вид: " + ex.Message;
                return false;
            }
        }

        private static Rect2 ViewportBounds(Viewport viewport)
        {
            using (Outline box = viewport.GetBoxOutline())
            using (Outline label = viewport.GetLabelOutline())
            {
                double minX = box.MinimumPoint.X, minY = box.MinimumPoint.Y;
                double maxX = box.MaximumPoint.X, maxY = box.MaximumPoint.Y;
                if (label != null && (label.MaximumPoint - label.MinimumPoint).GetLength() > 1e-9)
                {
                    minX = Math.Min(minX, label.MinimumPoint.X);
                    minY = Math.Min(minY, label.MinimumPoint.Y);
                    maxX = Math.Max(maxX, label.MaximumPoint.X);
                    maxY = Math.Max(maxY, label.MaximumPoint.Y);
                }
                return new Rect2(minX, minY, maxX, maxY);
            }
        }
    }
}

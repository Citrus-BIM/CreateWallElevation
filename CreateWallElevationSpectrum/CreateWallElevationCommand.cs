using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
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
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            Selection sel = uiDoc.Selection;

            List<ViewSheet> viewSheetList = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .OrderBy(vs => vs.SheetNumber, new AlphanumComparatorFastString())
                .ToList();

            // Вызов формы
            CreateWallElevationWPF wpf = new CreateWallElevationWPF(doc, viewSheetList);
            wpf.ShowDialog();
            if (wpf.DialogResult != true)
                return Result.Cancelled;

            ViewFamilyType selectedViewFamilyType = wpf.SelectedViewFamilyType;
            string selectedBuildByName = wpf.SelectedBuildByName;
            string selectedUseToBuildName = wpf.SelectedUseToBuildName;

            double indent = wpf.Indent;
            double indentUp = wpf.IndentUp;
            double indentDown = wpf.IndentDown;
            double projectionDepth = wpf.ProjectionDepth;

            bool useTemplate = wpf.UseTemplate;
            ViewSection viewSectionTemplate = wpf.ViewSectionTemplate;
            int curveNumberOfSegments = wpf.CurveNumberOfSegments;
            ViewSheet selectedViewSheet = wpf.SelectedViewSheet;

            // НОВОЕ: минимальная длина сегмента для упрощения контура помещений (ft)
            double minSegLenFt = wpf.MinSegmentLength;
            if (minSegLenFt <= 0)
            {
                TaskDialog.Show("Revit",
                    "В параметр \"Мин. длина сегмента\" введено недопустимое значение! Будет использовано значение 1000 мм.");
#if R2019 || R2020 || R2021
                minSegLenFt = UnitUtils.ConvertToInternalUnits(1000, DisplayUnitType.DUT_MILLIMETERS);
#else
                minSegLenFt = UnitUtils.ConvertToInternalUnits(1000, UnitTypeId.Millimeters);
#endif
            }

            if (projectionDepth == 0)
            {
                TaskDialog.Show("Revit",
                    "В параметр \"Глубина проекции\" введено недопустимое значение! Будет использовано значение по умолчанию (500 мм)!");
#if R2019 || R2020 || R2021
                projectionDepth = UnitUtils.ConvertToInternalUnits(500, DisplayUnitType.DUT_MILLIMETERS);
#else
                projectionDepth = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);
#endif
            }

            using (Transaction t = new Transaction(doc))
            {
                t.Start("Развертка стен");

                if (selectedBuildByName == "rbt_ByRoom")
                {
                    Result r = BuildByRooms(doc,
                        sel,
                        selectedUseToBuildName,
                        selectedViewFamilyType,
                        indent, indentUp, indentDown,
                        projectionDepth,
                        useTemplate, viewSectionTemplate,
                        curveNumberOfSegments,
                        selectedViewSheet,
                        minSegLenFt);

                    if (r != Result.Succeeded)
                    {
                        t.RollBack();
                        return r;
                    }
                }
                else
                {
                    Result r = BuildByWall(doc,
                        sel,
                        selectedUseToBuildName,
                        selectedViewFamilyType,
                        indent, indentUp, indentDown,
                        projectionDepth,
                        useTemplate, viewSectionTemplate,
                        curveNumberOfSegments,
                        selectedViewSheet);

                    if (r != Result.Succeeded)
                    {
                        t.RollBack();
                        return r;
                    }
                }

                t.Commit();
            }

            return Result.Succeeded;
        }

        private static Result BuildByRooms(
            Document doc,
            Selection sel,
            string selectedUseToBuildName,
            ViewFamilyType selectedViewFamilyType,
            double indent,
            double indentUp,
            double indentDown,
            double projectionDepth,
            bool useTemplate,
            ViewSection viewSectionTemplate,
            int curveNumberOfSegments,
            ViewSheet selectedViewSheet,
            double minSegLenFt)
        {
            List<Room> roomList = GetRoomsFromCurrentSelection(doc, sel);

            if (roomList.Count == 0)
            {
                RoomSelectionFilter selFilter = new RoomSelectionFilter();
                IList<Reference> selRooms;
                try
                {
                    selRooms = sel.PickObjects(ObjectType.Element, selFilter, "Выберите помещения!");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                foreach (Reference roomRef in selRooms)
                {
                    Room r = doc.GetElement(roomRef) as Room;
                    if (r != null) roomList.Add(r);
                }

                roomList = roomList
                    .OrderBy(r => r.Number, new AlphanumComparatorFastString())
                    .ThenBy(r => r.Name, new AlphanumComparatorFastString())
                    .ToList();
            }

            if (roomList.Count == 0)
                return Result.Cancelled;

            // страховки
            if (indent < 0) indent = 0;
            if (indentUp < 0) indentUp = 0;
            if (indentDown < 0) indentDown = 0;

            // глубина "вперёд" в координатах вида (по оси viewdir)
            double depthForward = projectionDepth - indent;

#if R2019 || R2020 || R2021
            double minDepth = UnitUtils.ConvertToInternalUnits(50, DisplayUnitType.DUT_MILLIMETERS);
#else
    double minDepth = UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters);
#endif

            if (depthForward < minDepth)
                depthForward = minDepth;

            double additionalOffset = 0;

            foreach (Room room in roomList)
            {
                int cnt = 1;
                List<ViewSection> viewSectionsList = new List<ViewSection>();

                XYZ roomCenter = GetRoomCenter(room);

                // высота по параметрам помещения
                GetRoomVerticalExtents(doc, room, out double baseZ, out double topZ);

                // запас по высоте
                double yMin = baseZ - indentDown;
                double yMax = topZ + indentUp;
                if (yMax <= yMin + Eps)
                {
#if R2019 || R2020 || R2021
                    yMax = yMin + UnitUtils.ConvertToInternalUnits(100, DisplayUnitType.DUT_MILLIMETERS);
#else
            yMax = yMin + UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters);
#endif
                }

                // упрощённый контур
                List<Curve> roomCurves = BoundarySimplifier.SimplifyRoomOuterLoop(room, minSegLenFt);
                if (roomCurves == null || roomCurves.Count == 0)
                    continue;

                bool isSectionMode = (selectedUseToBuildName == "rbt_Section");

                foreach (Curve curve in roomCurves)
                {
                    if (curve is Line ln)
                    {
                        XYZ a = ln.GetEndPoint(0);
                        XYZ b = ln.GetEndPoint(1);

                        double w = a.DistanceTo(b);
                        if (w <= Eps) continue;

                        XYZ curveDir = (b - a).Normalize();
                        XYZ origin = (a + b) * 0.5;

                        // inward = внутрь помещения
                        XYZ inward = GetInwardNormal(curveDir, roomCenter - origin);

                        if (isSectionMode)
                        {
                            // =========================
                            // РАЗРЕЗ по прямой
                            // viewdir = -inward
                            // =========================
                            XYZ up = XYZ.BasisZ;
                            XYZ viewdir = inward.Negate();
                            XYZ right = up.CrossProduct(viewdir).Normalize();

                            Transform tr = Transform.Identity;
                            tr.Origin = new XYZ(origin.X, origin.Y, 0);
                            tr.BasisX = right;
                            tr.BasisY = up;
                            tr.BasisZ = viewdir;

                            BoundingBoxXYZ bb = new BoundingBoxXYZ();
                            bb.Transform = tr;

                            bb.Min = new XYZ(-w / 2, yMin, -indent);
                            bb.Max = new XYZ(w / 2, yMax, depthForward);

                            ViewSection vs = ViewSection.CreateSection(doc, selectedViewFamilyType.Id, bb);
                            vs.Name = $"Р_П{room.Number}_{cnt}_{vs.Id}";
                            cnt++;

                            if (useTemplate && viewSectionTemplate != null)
                                vs.get_Parameter(BuiltInParameter.VIEW_TEMPLATE).Set(viewSectionTemplate.Id);

                            viewSectionsList.Add(vs);
                        }
                        else
                        {
                            // =========================
                            // ФАСАД по прямой
                            // =========================
                            XYZ markerPoint = origin + inward * indent;
                            XYZ viewdir = inward.Negate();

                            ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, selectedViewFamilyType.Id, markerPoint, 100);
                            ViewSection vs = marker.CreateElevation(doc, doc.ActiveView.Id, 0);

                            double ang = viewdir.AngleOnPlaneTo(vs.ViewDirection, XYZ.BasisZ);
                            if (Math.Abs(ang) > 1e-9)
                            {
                                ElementTransformUtils.RotateElement(
                                    doc,
                                    marker.Id,
                                    Line.CreateBound(markerPoint, markerPoint + XYZ.BasisZ),
                                    -ang);
                            }

                            vs.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR).Set(projectionDepth);

                            ViewCropRegionShapeManager crsm = vs.GetCropRegionShapeManager();

                            XYZ up = XYZ.BasisZ;
                            XYZ right = up.CrossProduct(inward).Normalize();

                            XYZ p1 = new XYZ(markerPoint.X, markerPoint.Y, yMin) - (w / 2) * right;
                            XYZ p2 = new XYZ(p1.X, p1.Y, yMax);
                            XYZ p3 = p2 + w * right;
                            XYZ p4 = new XYZ(p3.X, p3.Y, p1.Z);

                            var crop = new List<Curve>
                    {
                        Line.CreateBound(p1, p2),
                        Line.CreateBound(p2, p3),
                        Line.CreateBound(p3, p4),
                        Line.CreateBound(p4, p1)
                    };

                            crsm.SetCropShape(CurveLoop.Create(crop));
                            doc.Regenerate();

                            vs.Name = $"Ф_П{room.Number}_{cnt}_{vs.Id}";
                            cnt++;

                            if (useTemplate && viewSectionTemplate != null)
                                vs.get_Parameter(BuiltInParameter.VIEW_TEMPLATE).Set(viewSectionTemplate.Id);

                            viewSectionsList.Add(vs);
                        }
                    }
                    else if (curve is Arc arc)
                    {
                        // =========================
                        // ДУГА: сегментация
                        // =========================
                        XYZ center = arc.Center;

                        XYZ start0 = arc.GetEndPoint(0);
                        XYZ end0 = arc.GetEndPoint(1);

                        // тестовые точки для выбора стороны
                        XYZ sTest = start0 + indent * (center - start0).Normalize();
                        XYZ eTest = end0 + indent * (center - end0).Normalize();

                        XYZ startVector = (sTest - center).Normalize();
                        XYZ endVector = (eTest - center).Normalize();

                        double startEndAngle = endVector.AngleTo(startVector);
                        if (curveNumberOfSegments < 1) curveNumberOfSegments = 1;
                        double anglePerSegment = startEndAngle / curveNumberOfSegments;

                        XYZ mid = (sTest + eTest) * 0.5;
                        XYZ v1 = (mid - center).Normalize();
                        XYZ v2 = (mid - roomCenter).Normalize();
                        double angleDeg = v1.AngleTo(v2) * (180 / Math.PI);

                        List<XYZ> arcPoints = new List<XYZ>();

                        if (angleDeg <= 45)
                        {
                            XYZ s = start0 + indent * (center - start0).Normalize();
                            XYZ e = end0 + indent * (center - end0).Normalize();

                            arcPoints.Add(s);
                            for (int i = 1; i < curveNumberOfSegments; i++)
                            {
                                Transform rot = Transform.CreateRotationAtPoint(XYZ.BasisZ, i * anglePerSegment, center);
                                arcPoints.Add(rot.OfPoint(s));
                            }
                            arcPoints.Add(e);
                        }
                        else
                        {
                            XYZ s = start0 - indent * (center - start0).Normalize();
                            XYZ e = end0 - indent * (center - end0).Normalize();

                            arcPoints.Add(s);
                            for (int i = 1; i < curveNumberOfSegments; i++)
                            {
                                Transform rot = Transform.CreateRotationAtPoint(XYZ.BasisZ, -i * anglePerSegment, center);
                                arcPoints.Add(rot.OfPoint(s));
                            }
                            arcPoints.Add(e);
                        }

                        for (int i = 0; i < arcPoints.Count - 1; i++)
                        {
                            XYZ a = arcPoints[i];
                            XYZ b = arcPoints[i + 1];

                            double w = a.DistanceTo(b);
                            if (w <= Eps) continue;

                            XYZ curveDir = (b - a).Normalize();
                            XYZ origin = (a + b) * 0.5;

                            XYZ inward = GetInwardNormal(curveDir, roomCenter - origin);

                            if (isSectionMode)
                            {
                                // =========================
                                // РАЗРЕЗ сегментом дуги
                                // viewdir = -inward
                                // =========================
                                XYZ up = XYZ.BasisZ;
                                XYZ viewdir = inward.Negate();
                                XYZ right = up.CrossProduct(viewdir).Normalize();

                                Transform tr = Transform.Identity;
                                tr.Origin = new XYZ(origin.X, origin.Y, 0);
                                tr.BasisX = right;
                                tr.BasisY = up;
                                tr.BasisZ = viewdir;

                                BoundingBoxXYZ bb = new BoundingBoxXYZ();
                                bb.Transform = tr;
                                bb.Min = new XYZ(-w / 2, yMin, 0);
                                bb.Max = new XYZ(w / 2, yMax, projectionDepth);

                                ViewSection vs = ViewSection.CreateSection(doc, selectedViewFamilyType.Id, bb);
                                vs.Name = $"Р_П{room.Number}_{cnt}_{vs.Id}";
                                cnt++;

                                if (useTemplate && viewSectionTemplate != null)
                                    vs.get_Parameter(BuiltInParameter.VIEW_TEMPLATE).Set(viewSectionTemplate.Id);

                                viewSectionsList.Add(vs);
                            }
                            else
                            {
                                // =========================
                                // ФАСАД сегментом дуги
                                // =========================
                                XYZ markerPoint = origin + inward * indent;
                                XYZ viewdir = inward.Negate();

                                ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, selectedViewFamilyType.Id, markerPoint, 100);
                                ViewSection vs = marker.CreateElevation(doc, doc.ActiveView.Id, 0);

                                double ang = viewdir.AngleOnPlaneTo(vs.ViewDirection, XYZ.BasisZ);
                                if (Math.Abs(ang) > 1e-9)
                                {
                                    ElementTransformUtils.RotateElement(
                                        doc,
                                        marker.Id,
                                        Line.CreateBound(markerPoint, markerPoint + XYZ.BasisZ),
                                        -ang);
                                }

                                vs.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR).Set(projectionDepth);

                                ViewCropRegionShapeManager crsm = vs.GetCropRegionShapeManager();

                                XYZ up = XYZ.BasisZ;
                                XYZ right = up.CrossProduct(inward).Normalize();

                                XYZ p1 = new XYZ(markerPoint.X, markerPoint.Y, yMin) - (w / 2) * right;
                                XYZ p2 = new XYZ(p1.X, p1.Y, yMax);
                                XYZ p3 = p2 + w * right;
                                XYZ p4 = new XYZ(p3.X, p3.Y, p1.Z);

                                var crop = new List<Curve>
                        {
                            Line.CreateBound(p1, p2),
                            Line.CreateBound(p2, p3),
                            Line.CreateBound(p3, p4),
                            Line.CreateBound(p4, p1)
                        };

                                crsm.SetCropShape(CurveLoop.Create(crop));
                                doc.Regenerate();

                                vs.Name = $"Ф_П{room.Number}_{cnt}_{vs.Id}";
                                cnt++;

                                if (useTemplate && viewSectionTemplate != null)
                                    vs.get_Parameter(BuiltInParameter.VIEW_TEMPLATE).Set(viewSectionTemplate.Id);

                                viewSectionsList.Add(vs);
                            }
                        }
                    }
                }

                doc.Regenerate();

                if (selectedViewSheet != null && viewSectionsList.Count > 0)
                {
                    PlaceViewsOnSheet(doc, selectedViewSheet, viewSectionsList, ref additionalOffset);
                }
            }

            return Result.Succeeded;
        }

        private static void PlaceViewsOnSheet(Document doc, ViewSheet sheet, List<ViewSection> viewSectionsList, ref double additionalOffset)
        {
            List<FamilyInstance> titleBlocksList = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilyInstance>()
                .ToList();

            XYZ insertPoint = new XYZ(0, 0 - additionalOffset, 0);

            if (titleBlocksList.Count != 0)
            {
                FamilyInstance titleBlock = titleBlocksList.First();
                BoundingBoxXYZ bb = titleBlock.get_BoundingBox(sheet);

                double minX = bb.Min.X;
                double maxY = bb.Max.Y;

                insertPoint = new XYZ(minX + 30 / 304.8, maxY - 20 / 304.8 - additionalOffset, 0);
            }

            viewSectionsList.Reverse();

            double maxHight = 0;
            foreach (ViewSection viewSection in viewSectionsList)
            {
                Viewport viewport = Viewport.Create(doc, sheet.Id, viewSection.Id, insertPoint);

                int viewScale = viewSection.Scale;
                BoundingBoxXYZ cropbox = viewSection.CropBox;

                XYZ P1 = new XYZ(cropbox.Max.X / viewScale, cropbox.Max.Y / viewScale, 0);
                XYZ P2 = new XYZ(cropbox.Min.X / viewScale, cropbox.Min.Y / viewScale, 0);

                double deltaX = new XYZ(P1.X, 0, 0).DistanceTo(new XYZ(P2.X, 0, 0)) / 2;
                double deltaY = new XYZ(0, P1.Y, 0).DistanceTo(new XYZ(0, P2.Y, 0)) / 2;

                ElementTransformUtils.MoveElement(doc, viewport.Id, new XYZ(deltaX, -deltaY, 0));

                insertPoint = new XYZ(insertPoint.X + deltaX * 2, insertPoint.Y, insertPoint.Z);
                if (deltaY * 2 > maxHight)
                    maxHight = deltaY * 2;
            }

#if R2019 || R2020 || R2021
            additionalOffset += maxHight + UnitUtils.ConvertToInternalUnits(20, DisplayUnitType.DUT_MILLIMETERS);
#else
            additionalOffset += maxHight + UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Millimeters);
#endif
        }

        private static Result BuildByWall(
            Document doc,
            Selection sel,
            string selectedUseToBuildName,
            ViewFamilyType selectedViewFamilyType,
            double indent,
            double indentUp,
            double indentDown,
            double projectionDepth,
            bool useTemplate,
            ViewSection viewSectionTemplate,
            int curveNumberOfSegments,
            ViewSheet selectedViewSheet)
        {
            WallSelectionFilter wallSelFilter = new WallSelectionFilter();
            Reference selWall;
            Wall wall;
            XYZ pickedPoint;

            try
            {
                selWall = sel.PickObject(ObjectType.Element, wallSelFilter, "Выберите стену!");
                wall = doc.GetElement(selWall) as Wall;
                pickedPoint = sel.PickPoint("Укажите сторону размещения");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (wall == null)
                return Result.Cancelled;

            // Дальше оставлено как у тебя (без изменений по логике)
            List<ViewSection> viewSectionsList = new List<ViewSection>();

            if (selectedUseToBuildName == "rbt_Section")
            {
                XYZ wallOrientation = wall.Flipped ? wall.Orientation.Negate() : wall.Orientation;
                Curve curve = (wall.Location as LocationCurve).Curve;

                if (curve is Line)
                {
                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);

                    XYZ pointH = curve.Project(new XYZ(pickedPoint.X, pickedPoint.Y, start.Z)).XYZPoint;
                    XYZ normalVector = (new XYZ(pickedPoint.X, pickedPoint.Y, start.Z) - pointH).Normalize();

                    if (!normalVector.IsAlmostEqualTo(wallOrientation))
                    {
                        start = curve.GetEndPoint(1);
                        end = curve.GetEndPoint(0);
                    }

                    XYZ curveDir = (end - start).Normalize();
                    double w = (end - start).GetLength();

                    Transform curveTransform = curve.ComputeDerivatives(0.5, true);
                    XYZ origin = curveTransform.Origin;
                    XYZ right = curveDir;
                    XYZ up = XYZ.BasisZ;
                    XYZ viewdir = curveDir.CrossProduct(up).Normalize();

                    BoundingBoxXYZ wallBb = wall.get_BoundingBox(null);
                    double minZ = wallBb.Min.Z;
                    double maxZ = wallBb.Max.Z;

                    Transform transform = Transform.Identity;
                    transform.Origin = new XYZ(origin.X, origin.Y, 0);
                    transform.BasisX = right;
                    transform.BasisY = up;
                    transform.BasisZ = viewdir;

                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                    sectionBox.Transform = transform;
                    sectionBox.Min = new XYZ(-w / 2, minZ + indentDown, -indent - wall.Width / 2);
                    sectionBox.Max = new XYZ(w / 2, maxZ + indentUp, projectionDepth - indent - wall.Width / 2);

                    ViewSection viewSection = ViewSection.CreateSection(doc, selectedViewFamilyType.Id, sectionBox);
                    viewSection.Name = $"Р_Ст_{viewSection.Id}";

                    if (useTemplate && viewSectionTemplate != null)
                        viewSection.get_Parameter(BuiltInParameter.VIEW_TEMPLATE).Set(viewSectionTemplate.Id);

                    viewSectionsList.Add(viewSection);
                }
                else
                {
                    // дуга стены — твой код без изменений
                    XYZ center = (curve as Arc).Center;
                    Curve pickPointLine = Line.CreateBound(center, pickedPoint) as Curve;

                    XYZ start;
                    XYZ end;

                    if (curve.Intersect(pickPointLine) != SetComparisonResult.Overlap)
                    {
                        start = curve.GetEndPoint(0) + indent * (center - curve.GetEndPoint(0)).Normalize() + wall.Width / 2 * (center - curve.GetEndPoint(0)).Normalize();
                        end = curve.GetEndPoint(1) + indent * (center - curve.GetEndPoint(1)).Normalize() + wall.Width / 2 * (center - curve.GetEndPoint(0)).Normalize();
                    }
                    else
                    {
                        start = curve.GetEndPoint(0) - indent * (center - curve.GetEndPoint(0)).Normalize() - wall.Width / 2 * (center - curve.GetEndPoint(0)).Normalize();
                        end = curve.GetEndPoint(1) - indent * (center - curve.GetEndPoint(1)).Normalize() - wall.Width / 2 * (center - curve.GetEndPoint(0)).Normalize();
                    }

                    XYZ arcNormal = (start - center).Normalize();

                    XYZ startVector = (start - center).Normalize();
                    XYZ endVector = (end - center).Normalize();

                    double startEndAngle = endVector.AngleTo(startVector);
                    double anglePerSegment = startEndAngle / curveNumberOfSegments;

                    List<XYZ> arcPoints = new List<XYZ>() { start };

                    if (arcNormal.IsAlmostEqualTo(wallOrientation))
                    {
                        for (int i = 1; i < curveNumberOfSegments; i++)
                        {
                            Transform rotationTransform = Transform.CreateRotationAtPoint(new XYZ(0, 0, 1), -i * anglePerSegment, center);
                            XYZ rotatedVector = rotationTransform.OfPoint(start);
                            arcPoints.Add(rotatedVector);
                        }
                    }
                    else
                    {
                        for (int i = 1; i < curveNumberOfSegments; i++)
                        {
                            Transform rotationTransform = Transform.CreateRotationAtPoint(new XYZ(0, 0, 1), i * anglePerSegment, center);
                            XYZ rotatedVector = rotationTransform.OfPoint(start);
                            arcPoints.Add(rotatedVector);
                        }
                    }

                    arcPoints.Add(end);

                    if (curve.Intersect(pickPointLine) != SetComparisonResult.Overlap)
                        arcPoints.Reverse();

                    if (!arcNormal.IsAlmostEqualTo(wallOrientation))
                        arcPoints.Reverse();

                    for (int i = 0; i < arcPoints.Count - 1; i++)
                    {
                        XYZ tmpStart = arcPoints[i];
                        XYZ tmpEnd = arcPoints[i + 1];
                        XYZ curveDir = (tmpEnd - tmpStart).Normalize();
                        double w = (tmpEnd - tmpStart).GetLength();

                        XYZ origin = (tmpStart + tmpEnd) / 2;
                        XYZ right = curveDir;
                        XYZ up = XYZ.BasisZ;
                        XYZ viewdir = curveDir.CrossProduct(up).Normalize();

                        BoundingBoxXYZ wallBb = wall.get_BoundingBox(null);
                        double minZ = wallBb.Min.Z;
                        double maxZ = wallBb.Max.Z;

                        Transform transform = Transform.Identity;
                        transform.Origin = new XYZ(origin.X, origin.Y, 0);
                        transform.BasisX = right;
                        transform.BasisY = up;
                        transform.BasisZ = viewdir;

                        BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                        sectionBox.Transform = transform;
                        sectionBox.Min = new XYZ(-w / 2, minZ + indentDown, 0);
                        sectionBox.Max = new XYZ(w / 2, maxZ + indentUp, projectionDepth);

                        ViewSection viewSection = ViewSection.CreateSection(doc, selectedViewFamilyType.Id, sectionBox);
                        viewSection.Name = $"Р_Ст_{viewSection.Id}";

                        if (useTemplate && viewSectionTemplate != null)
                            viewSection.get_Parameter(BuiltInParameter.VIEW_TEMPLATE).Set(viewSectionTemplate.Id);

                        viewSectionsList.Add(viewSection);
                    }
                }
            }
            else
            {
                // Фасады по стене — твой код без изменений
                XYZ wallOrientation = wall.Flipped ? wall.Orientation.Negate() : wall.Orientation;
                Curve curve = (wall.Location as LocationCurve).Curve;

                if (curve is Line)
                {
                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);

                    XYZ pointH = curve.Project(new XYZ(pickedPoint.X, pickedPoint.Y, start.Z)).XYZPoint;
                    XYZ normalVector = (new XYZ(pickedPoint.X, pickedPoint.Y, start.Z) - pointH).Normalize();

                    if (normalVector.IsAlmostEqualTo(wallOrientation))
                    {
                        start = curve.GetEndPoint(1);
                        end = curve.GetEndPoint(0);
                    }
                    else
                    {
                        start = curve.GetEndPoint(0);
                        end = curve.GetEndPoint(1);
                    }

                    XYZ curveDir = (end - start).Normalize();
                    double w = (end - start).GetLength();

                    Transform curveTransform = curve.ComputeDerivatives(0.5, true);
                    XYZ origin = curveTransform.Origin;
                    XYZ right = curveDir;
                    XYZ up = XYZ.BasisZ;
                    XYZ viewdir = curveDir.CrossProduct(up).Normalize();

                    BoundingBoxXYZ wallBb = wall.get_BoundingBox(null);
                    double minZ = wallBb.Min.Z;
                    double maxZ = wallBb.Max.Z;

                    origin = origin - (wall.Width / 2) * viewdir.Negate() - indent * viewdir.Negate();

                    ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, selectedViewFamilyType.Id, origin, 100);
                    ViewSection viewSection = marker.CreateElevation(doc, doc.ActiveView.Id, 0);

                    double angle = viewdir.AngleOnPlaneTo(viewSection.ViewDirection, XYZ.BasisZ);
                    if (angle != 0)
                    {
                        ElementTransformUtils.RotateElement(doc, marker.Id, Line.CreateBound(origin, origin + 1 * XYZ.BasisZ), -angle);
                    }

                    viewSection.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR).Set(projectionDepth);
                    ViewCropRegionShapeManager crsm = viewSection.GetCropRegionShapeManager();

                    XYZ p1 = new XYZ(origin.X, origin.Y, minZ + indentDown) - w / 2 * right;
                    XYZ p2 = new XYZ(p1.X, p1.Y, maxZ + indentUp);
                    XYZ p3 = p2 + w * right;
                    XYZ p4 = new XYZ(p3.X, p3.Y, p1.Z);

                    List<Curve> curveList = new List<Curve>();
                    curveList.Add(Line.CreateBound(p1, p2));
                    curveList.Add(Line.CreateBound(p2, p3));
                    curveList.Add(Line.CreateBound(p3, p4));
                    curveList.Add(Line.CreateBound(p4, p1));

                    CurveLoop curveLoop = CurveLoop.Create(curveList);
                    crsm.SetCropShape(curveLoop);

                    doc.Regenerate();

                    viewSection.Name = $"Ф_Ст_{viewSection.Id}";

                    if (useTemplate && viewSectionTemplate != null)
                        viewSection.get_Parameter(BuiltInParameter.VIEW_TEMPLATE).Set(viewSectionTemplate.Id);

                    viewSectionsList.Add(viewSection);
                }
                else
                {
                    XYZ center = (curve as Arc).Center;
                    Curve pickPointLine = Line.CreateBound(center, pickedPoint) as Curve;

                    XYZ start;
                    XYZ end;

                    if (curve.Intersect(pickPointLine) != SetComparisonResult.Overlap)
                    {
                        start = curve.GetEndPoint(0) + indent * (center - curve.GetEndPoint(0)).Normalize() + wall.Width / 2 * (center - curve.GetEndPoint(0)).Normalize();
                        end = curve.GetEndPoint(1) + indent * (center - curve.GetEndPoint(1)).Normalize() + wall.Width / 2 * (center - curve.GetEndPoint(0)).Normalize();
                    }
                    else
                    {
                        start = curve.GetEndPoint(0) - indent * (center - curve.GetEndPoint(0)).Normalize() - wall.Width / 2 * (center - curve.GetEndPoint(0)).Normalize();
                        end = curve.GetEndPoint(1) - indent * (center - curve.GetEndPoint(1)).Normalize() - wall.Width / 2 * (center - curve.GetEndPoint(0)).Normalize();
                    }

                    XYZ arcNormal = (start - center).Normalize();

                    XYZ startVector = (start - center).Normalize();
                    XYZ endVector = (end - center).Normalize();

                    double startEndAngle = endVector.AngleTo(startVector);
                    double anglePerSegment = startEndAngle / curveNumberOfSegments;

                    List<XYZ> arcPoints = new List<XYZ>() { start };

                    if (arcNormal.IsAlmostEqualTo(wallOrientation))
                    {
                        for (int i = 1; i < curveNumberOfSegments; i++)
                        {
                            Transform rotationTransform = Transform.CreateRotationAtPoint(new XYZ(0, 0, 1), -i * anglePerSegment, center);
                            XYZ rotatedVector = rotationTransform.OfPoint(start);
                            arcPoints.Add(rotatedVector);
                        }
                    }
                    else
                    {
                        for (int i = 1; i < curveNumberOfSegments; i++)
                        {
                            Transform rotationTransform = Transform.CreateRotationAtPoint(new XYZ(0, 0, 1), i * anglePerSegment, center);
                            XYZ rotatedVector = rotationTransform.OfPoint(start);
                            arcPoints.Add(rotatedVector);
                        }
                    }

                    arcPoints.Add(end);

                    if (curve.Intersect(pickPointLine) != SetComparisonResult.Overlap)
                        arcPoints.Reverse();

                    if (!arcNormal.IsAlmostEqualTo(wallOrientation))
                        arcPoints.Reverse();

                    for (int i = 0; i < arcPoints.Count - 1; i++)
                    {
                        XYZ tmpStart = arcPoints[i];
                        XYZ tmpEnd = arcPoints[i + 1];
                        XYZ curveDir = (tmpEnd - tmpStart).Normalize();
                        double w = (tmpEnd - tmpStart).GetLength();

                        XYZ origin = (tmpStart + tmpEnd) / 2;
                        XYZ right = curveDir;
                        XYZ up = XYZ.BasisZ;
                        XYZ viewdir = curveDir.CrossProduct(up).Normalize().Negate();

                        BoundingBoxXYZ wallBb = wall.get_BoundingBox(null);
                        double minZ = wallBb.Min.Z;
                        double maxZ = wallBb.Max.Z;

                        ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, selectedViewFamilyType.Id, origin, 100);
                        ViewSection viewSection = marker.CreateElevation(doc, doc.ActiveView.Id, 0);

                        double angle = viewdir.AngleOnPlaneTo(viewSection.ViewDirection, XYZ.BasisZ);
                        if (angle != 0)
                        {
                            ElementTransformUtils.RotateElement(doc, marker.Id, Line.CreateBound(origin, origin + 1 * XYZ.BasisZ), -angle);
                        }

                        viewSection.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR).Set(projectionDepth);
                        ViewCropRegionShapeManager crsm = viewSection.GetCropRegionShapeManager();

                        XYZ p1 = new XYZ(origin.X, origin.Y, minZ + indentDown) - w / 2 * right;
                        XYZ p2 = new XYZ(p1.X, p1.Y, maxZ + indentUp);
                        XYZ p3 = p2 + w * right;
                        XYZ p4 = new XYZ(p3.X, p3.Y, p1.Z);

                        List<Curve> curveList = new List<Curve>();
                        curveList.Add(Line.CreateBound(p1, p2));
                        curveList.Add(Line.CreateBound(p2, p3));
                        curveList.Add(Line.CreateBound(p3, p4));
                        curveList.Add(Line.CreateBound(p4, p1));

                        CurveLoop curveLoop = CurveLoop.Create(curveList);
                        crsm.SetCropShape(curveLoop);

                        doc.Regenerate();

                        viewSection.Name = $"Ф_Ст_{viewSection.Id}";

                        if (useTemplate && viewSectionTemplate != null)
                            viewSection.get_Parameter(BuiltInParameter.VIEW_TEMPLATE).Set(viewSectionTemplate.Id);

                        viewSectionsList.Add(viewSection);
                    }
                }
            }

            doc.Regenerate();

            if (selectedViewSheet != null)
            {
                // В режиме "по стене" раскладка без additionalOffset (как было)
                double dummyOffset = 0;
                PlaceViewsOnSheet(doc, selectedViewSheet, viewSectionsList, ref dummyOffset);
            }

            return Result.Succeeded;
        }

        private static List<Room> GetRoomsFromCurrentSelection(Document doc, Selection sel)
        {
            ICollection<ElementId> selectedIds = sel.GetElementIds();
            List<Room> tempRoomsList = new List<Room>();

            foreach (ElementId roomId in selectedIds)
            {
                Element e = doc.GetElement(roomId);
                if (e is Room && e.Category != null &&
                    e.Category.Id.IntegerValue.Equals((int)BuiltInCategory.OST_Rooms))
                {
                    tempRoomsList.Add(e as Room);
                }
            }

            tempRoomsList = tempRoomsList
                .OrderBy(r => r.Number, new AlphanumComparatorFastString())
                .ThenBy(r => r.Name, new AlphanumComparatorFastString())
                .ToList();

            return tempRoomsList;
        }

        private const double Eps = 1e-6;

        private static XYZ GetRoomCenter(Room room)
        {
            var lp = room.Location as LocationPoint;
            if (lp != null) return lp.Point;

            var bb = room.get_BoundingBox(null);
            if (bb != null) return (bb.Min + bb.Max) * 0.5;

            return XYZ.Zero;
        }

        /// <summary>
        /// Возвращает нормаль в плоскости XY, направленную ВНУТРЬ помещения (к roomCenter).
        /// </summary>
        private static XYZ GetInwardNormal(XYZ curveDir, XYZ toCenter)
        {
            XYZ n = curveDir.CrossProduct(XYZ.BasisZ);
            if (n.GetLength() < Eps) return XYZ.BasisY;

            n = n.Normalize();
            if (n.DotProduct(toCenter) < 0) n = n.Negate();
            return n;
        }

        /// <summary>
        /// Вертикальные границы помещения по параметрам (а не bbox).
        /// baseZ = Level.Elevation + ROOM_LOWER_OFFSET
        /// topZ  = baseZ + ROOM_HEIGHT (fallback: UnboundedHeight, fallback: bbox)
        /// </summary>
        private static void GetRoomVerticalExtents(Document doc, Room room, out double baseZ, out double topZ)
        {
            baseZ = 0;
            topZ = 0;

            var level = doc.GetElement(room.LevelId) as Level;
            double levelZ = level != null ? level.Elevation : 0;

            double lowerOffset = 0;
            var pLower = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
            if (pLower != null && pLower.StorageType == StorageType.Double)
                lowerOffset = pLower.AsDouble();

            baseZ = levelZ + lowerOffset;

            double h = 0;
            var pHeight = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
            if (pHeight != null && pHeight.StorageType == StorageType.Double)
                h = pHeight.AsDouble();

            if (h <= Eps)
            {
                try { h = room.UnboundedHeight; } catch { h = 0; }
            }

            if (h <= Eps)
            {
                // последний fallback — bbox
                var bb = room.get_BoundingBox(null);
                if (bb != null)
                {
                    baseZ = bb.Min.Z;
                    topZ = bb.Max.Z;
                    return;
                }

                // совсем аварийно
#if R2019 || R2020 || R2021
                h = UnitUtils.ConvertToInternalUnits(3000, DisplayUnitType.DUT_MILLIMETERS);
#else
                h = UnitUtils.ConvertToInternalUnits(3000, UnitTypeId.Millimeters);
#endif
            }

            topZ = baseZ + h;
        }
    }
}

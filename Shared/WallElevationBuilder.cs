using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CreateWallElevation
{
    internal sealed class WallElevationBuilder
    {
        private readonly Document doc;
        private readonly CreateWallElevationWPF options;
        private readonly bool sections;
        private readonly List<string> notes = new List<string>();
        private int created, completed, failed, unplaced;

        private sealed class FaceSegment
        {
            public XYZ Observer, ObserverDirection, SectionBoxDirection;
            public List<XYZ> SurfacePoints;
            public double Bottom, Top;
        }

        private sealed class WorkItem
        {
            public string Label, NamePrefix;
            public Func<List<FaceSegment>> Prepare;
        }

        public WallElevationBuilder(Document document, CreateWallElevationWPF settings)
        {
            doc = document;
            options = settings;
            sections = settings.SelectedUseToBuildName == "rbt_Section";
        }

        public Result Run(UIDocument uiDocument)
        {
            ValidateContext();
            // Interactive selection never holds a write transaction open.
            List<WorkItem> items = SelectItems(uiDocument.Selection);
            if (items.Count == 0) return Result.Cancelled;
            using (var group = new TransactionGroup(doc, "Развёртки стен"))
            {
                group.Start();
                foreach (WorkItem item in items)
                {
                    try
                    {
                        List<FaceSegment> segments = item.Prepare();
                        if (segments.Count == 0)
                        {
                            failed++;
                            notes.Add(item.Label + ": нет подходящих границ после упрощения.");
                            continue;
                        }
                        using (var transaction = new Transaction(doc, "Развёртки: " + item.Label))
                        {
                            transaction.Start();
                            var failures = new ItemFailures();
                            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                                .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
                            var views = new List<ViewSection>();
                            var placementNotes = new List<string>();
                            int itemUnplaced = 0;
                            foreach (FaceSegment segment in segments)
                            {
                                ViewSection view = CreateView(segment);
                                view.Name = item.NamePrefix + "_" + (views.Count + 1) + "_" + view.Id;
                                views.Add(view);
                            }
                            if (options.SelectedViewSheet != null)
                            {
                                foreach (ViewSection view in views)
                                    if (!SheetLayout.Place(doc, options.SelectedViewSheet, view, out string reason))
                                    {
                                        itemUnplaced++;
                                        placementNotes.Add(view.Name + ": " + reason);
                                    }
                            }
                            TransactionStatus status = transaction.Commit();
                            if (status == TransactionStatus.Pending)
                                throw new ApplicationException("Revit ожидает завершения обработки ошибок. Пакет остановлен.");
                            if (status != TransactionStatus.Committed)
                            {
                                failed++;
                                notes.Add(item.Label + ": изменения отменены Revit. " + failures.Description);
                                continue;
                            }
                            created += views.Count;
                            completed++;
                            unplaced += itemUnplaced;
                            notes.AddRange(placementNotes);
                        }
                    }
                    catch (Exception ex) when (IsItemError(ex))
                    {
                        // Disposing an uncommitted transaction rolls back only this item.
                        failed++;
                        notes.Add(item.Label + ": " + ex.Message);
                    }
                }
                if (created == 0) group.RollBack();
                else if (group.Assimilate() != TransactionStatus.Committed)
                    throw new ApplicationException("Не удалось завершить группу транзакций развёрток.");
            }
            var summary = new TaskDialog("Развёртки стен")
            {
                MainInstruction = "Создано видов: " + created,
                MainContent = "Обработано объектов: " + completed + ". Пропущено или с ошибками: " + failed + "." +
                    (unplaced > 0 ? "\nБез размещения на листе: " + unplaced + ". Виды сохранены в диспетчере проекта." : ""),
                ExpandedContent = string.Join(Environment.NewLine, notes)
            };
            summary.Show();
            return created > 0 ? Result.Succeeded : Result.Cancelled;
        }

        internal static bool IsItemError(Exception ex) => !(ex is Autodesk.Revit.Exceptions.RegenerationFailedException) &&
            (ex is ArgumentException || ex is InvalidOperationException ||
            ex is Autodesk.Revit.Exceptions.ArgumentException || ex is Autodesk.Revit.Exceptions.InvalidOperationException);

        private void ValidateContext()
        {
            if (doc.IsFamilyDocument) throw new ArgumentException("Откройте документ проекта Revit.");
            if (options.SelectedViewFamilyType == null || options.SelectedViewFamilyType.ViewFamily !=
                (sections ? ViewFamily.Section : ViewFamily.Elevation))
                throw new ArgumentException("Выбранный тип вида не соответствует режиму построения.");
            if (!sections && (!(doc.ActiveView is ViewPlan) || doc.ActiveView.IsTemplate))
                throw new ArgumentException("Для фасадов необходимо открыть план этажа.");
            foreach (double value in new[] { options.Indent, options.IndentUp, options.IndentDown, options.ProjectionDepth, options.MinSegmentLength })
                if (!Geometry2D.IsFinite(value) || value < 0) throw new ArgumentException("Недопустимые отступы или глубина проекции.");
            if (options.ProjectionDepth <= options.Indent || options.ProjectionDepth <= doc.Application.ShortCurveTolerance)
                throw new ArgumentException("Глубина проекции должна быть больше отступа от грани.");
            Geometry2D.SegmentParameters(options.CurveNumberOfSegments);
        }

        private List<WorkItem> SelectItems(Selection selection)
        {
            var items = new List<WorkItem>();
            if (options.SelectedBuildByName == "rbt_ByRoom")
            {
                var rooms = selection.GetElementIds().Select(doc.GetElement).OfType<Room>().ToList();
                if (rooms.Count == 0)
                    rooms = selection.PickObjects(ObjectType.Element, new RoomSelectionFilter(), "Выберите помещения")
                        .Select(r => doc.GetElement(r)).OfType<Room>().ToList();
                foreach (Room room in rooms.OrderBy(r => r.Number, new AlphanumComparatorFastString()).ThenBy(r => r.Name))
                {
                    Room current = room;
                    items.Add(new WorkItem
                    {
                        Label = "Помещение " + current.Number + " (" + current.Id + ")",
                        NamePrefix = (sections ? "Р_П" : "Ф_П") + SafeName(current.Number),
                        Prepare = () => RoomSegments(current)
                    });
                }
            }
            else
            {
                var reference = selection.PickObject(ObjectType.Element, new WallSelectionFilter(), "Выберите стену");
                var wall = doc.GetElement(reference) as Wall;
                XYZ point = selection.PickPoint("Укажите сторону размещения");
                items.Add(new WorkItem
                {
                    Label = "Стена " + wall.Id,
                    NamePrefix = sections ? "Р_Ст" : "Ф_Ст",
                    Prepare = () => WallSegments(wall, point)
                });
            }
            return items;
        }

        private List<FaceSegment> RoomSegments(Room room)
        {
            if (room.Location == null || room.Area <= 0)
                throw new ArgumentException("Помещение не размещено или не замкнуто.");
            var level = doc.GetElement(room.LevelId) as Level;
            if (level == null) throw new ArgumentException("У помещения отсутствует уровень.");
            if (!sections)
            {
                var plan = (ViewPlan)doc.ActiveView;
                if (plan.GenLevel == null || !plan.GenLevel.Id.Equals(room.LevelId))
                    throw new ArgumentException("Откройте план уровня выбранного помещения.");
                ElementId roomPhase = room.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId();
                ElementId planPhase = plan.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();
                if (roomPhase != null && planPhase != null && !roomPhase.Equals(planPhase))
                    throw new ArgumentException("Фаза активного плана не совпадает с фазой помещения.");
            }
            double bottom = level.ProjectElevation + (room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET)?.AsDouble() ?? 0);
            double height = room.UnboundedHeight;
            if (height <= doc.Application.ShortCurveTolerance)
                throw new ArgumentException("У помещения некорректная высота.");
            var originalLoops = BoundarySimplifier.GetRoomLoops(room);
            IList<IList<Point2>> planLoops = originalLoops.Select(loop => (IList<Point2>)PlanPoints(loop)).ToList();
            Func<Point2, bool> insidePlan = p => Geometry2D.IsInsideLoops(p, planLoops);
            var result = new List<FaceSegment>();
            foreach (var loop in originalLoops.Select(loop => BoundarySimplifier.SimplifyCurves(loop,
                options.MinSegmentLength, RevitUnits.FromMillimeters(1), 1e-6, true)))
                foreach (Curve curve in loop)
                    result.AddRange(SegmentCurve(curve, bottom, bottom + height, (point, tangent) =>
                        Geometry2D.InteriorNormal(point, tangent, insidePlan, RevitUnits.FromMillimeters(50)), 0,
                        observer => options.Indent == 0 || insidePlan(observer)));
            return result;
        }

        private static List<Point2> PlanPoints(List<Curve> loop)
        {
            var points = new List<Point2>();
            foreach (Curve curve in loop)
            {
                if (!(curve is Line || curve is Arc) || !curve.IsBound)
                    throw new ArgumentException("Поддерживаются замкнутые контуры из прямых и дуговых границ.");
                int count = 1;
                if (curve is Arc arc)
                {
                    // Bound chord error to 0.1 mm; use these contours only for local side/placement tests.
                    double step = 2 * Math.Acos(Math.Max(0, 1 - RevitUnits.FromMillimeters(0.1) / arc.Radius));
                    double required = Math.Ceiling(arc.Length / arc.Radius / step);
                    if (!Geometry2D.IsFinite(required) || required > 20000)
                        throw new ArgumentException("Радиус границы слишком велик для надёжного определения стороны.");
                    count = Math.Max(2, (int)required);
                }
                for (int i = 0; i < count; i++) points.Add(ToPoint(curve.Evaluate((double)i / count, true)));
            }
            return points;
        }

        private List<FaceSegment> WallSegments(Wall wall, XYZ pickedPoint)
        {
            var location = wall.Location as LocationCurve;
            var bounds = wall.get_BoundingBox(null);
            if (location == null || bounds == null || !(location.Curve is Line || location.Curve is Arc))
                throw new ArgumentException("Поддерживаются стены с прямой или дуговой осью.");
            Curve curve = location.Curve;
            XYZ pickedOnPlane = new XYZ(pickedPoint.X, pickedPoint.Y, curve.GetEndPoint(0).Z);
            IntersectionResult projection = curve.Project(pickedOnPlane);
            if (projection == null) throw new ArgumentException("Не удалось определить сторону стены.");
            XYZ tangent = curve.ComputeDerivatives(projection.Parameter, false).BasisX;
            Point2 normal = new Point2(tangent.Y, -tangent.X).Normalized();
            double sideDistance = normal.Dot(ToPoint(pickedOnPlane - projection.XYZPoint));
            if (Math.Abs(sideDistance) <= RevitUnits.FromMillimeters(1))
                throw new ArgumentException("Укажите точку сбоку от стены, а не на её оси.");
            double sign = Math.Sign(sideDistance);
            return SegmentCurve(curve, bounds.Min.Z, bounds.Max.Z,
                (point, direction) => new Point2(direction.Y, -direction.X).Normalized() * sign,
                wall.Width / 2, observer => true);
        }

        private List<FaceSegment> SegmentCurve(Curve curve, double bottom, double top,
            Func<Point2, Point2, Point2> normalAt, double surfaceOffset, Func<Point2, bool> observerIsValid)
        {
            if (!curve.IsBound) throw new ArgumentException("Незамкнутая параметрическая граница не поддерживается.");
            int count = curve is Line ? 1 : options.CurveNumberOfSegments;
            if (curve is Arc arc && arc.Length / arc.Radius / count > Math.PI / 2 + 1e-6)
                throw new ArgumentException("Увеличьте число сегментов: один сегмент дуги не должен превышать 90°.");
            var vertical = Geometry2D.VerticalRange(bottom, top, options.IndentDown, options.IndentUp);
            var prepared = Geometry2D.PrepareSegments(t => ToPoint(curve.Evaluate(t, true)), t =>
            {
                XYZ direction = curve.ComputeDerivatives(t, true).BasisX;
                if (Math.Abs(direction.Normalize().Z) > 1e-6)
                    throw new ArgumentException("Граница должна лежать в горизонтальной плоскости.");
                return ToPoint(direction);
            }, normalAt, count, surfaceOffset, options.Indent, options.ProjectionDepth, observerIsValid);
            return prepared.Select(segment => new FaceSegment
            {
                Observer = new XYZ(segment.Frame.Observer.X, segment.Frame.Observer.Y, bottom),
                ObserverDirection = new XYZ(segment.Frame.ObserverDirection.X, segment.Frame.ObserverDirection.Y, 0),
                SectionBoxDirection = new XYZ(segment.Frame.SectionBoxDirection.X, segment.Frame.SectionBoxDirection.Y, 0),
                SurfacePoints = segment.SurfacePoints.Select(p => new XYZ(p.X, p.Y, 0)).ToList(),
                Bottom = vertical.Min, Top = vertical.Max
            }).ToList();
        }
        private ViewSection CreateView(FaceSegment segment)
        {
            XYZ observer = segment.Observer;
            XYZ direction = segment.ObserverDirection;
            ViewSection view;
            if (sections)
            {
                // CreateSection's box Z points into the model; ViewDirection of an elevation points to the observer.
                XYZ look = segment.SectionBoxDirection;
                XYZ right = XYZ.BasisZ.CrossProduct(look).Normalize();
                var horizontal = HorizontalRange(segment, right);
                Transform transform = Transform.Identity;
                transform.Origin = new XYZ(observer.X, observer.Y, 0);
                transform.BasisX = right;
                transform.BasisY = XYZ.BasisZ;
                transform.BasisZ = look;
                using (var box = new BoundingBoxXYZ())
                {
                    box.Transform = transform;
                    box.Min = new XYZ(horizontal.Min, segment.Bottom, 0);
                    box.Max = new XYZ(horizontal.Max, segment.Top, options.ProjectionDepth);
                    view = ViewSection.CreateSection(doc, options.SelectedViewFamilyType.Id, box);
                }
            }
            else
            {
                ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, options.SelectedViewFamilyType.Id, observer, 100);
                view = marker.CreateElevation(doc, doc.ActiveView.Id, 0);
                doc.Regenerate();
                double angle = view.ViewDirection.AngleOnPlaneTo(direction, XYZ.BasisZ);
                if (Math.Abs(angle) > 1e-9)
                    ElementTransformUtils.RotateElement(doc, marker.Id, Line.CreateBound(observer, observer + XYZ.BasisZ), angle);
                doc.Regenerate();
                if (view.ViewDirection.Normalize().DotProduct(direction) < 0.999999)
                    throw new InvalidOperationException("Не удалось направить фасад к стене.");
            }

            // The checkbox is authoritative even when the view family type has a default template.
            view.ViewTemplateId = ElementId.InvalidElementId;
            if (options.UseTemplate && options.ViewSectionTemplate != null)
            {
                if (!view.IsValidViewTemplate(options.ViewSectionTemplate.Id))
                    throw new ArgumentException("Выбранный шаблон несовместим с создаваемым видом.");
                view.ViewTemplateId = options.ViewSectionTemplate.Id;
                doc.Regenerate();
            }
            SetInteger(view, BuiltInParameter.VIEWER_CROP_REGION, 1);
            Parameter scopeBox = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (scopeBox != null && !scopeBox.AsElementId().Equals(ElementId.InvalidElementId))
            {
                if (scopeBox.IsReadOnly) throw TemplateConflict("Границы 3D вида (Scope Box)");
                scopeBox.Set(ElementId.InvalidElementId);
            }
            SetInteger(view, BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING, 1);
            Parameter far = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
            if (far == null) throw new InvalidOperationException("У вида нет параметра глубины проекции.");
            if (far.IsReadOnly && Math.Abs(far.AsDouble() - options.ProjectionDepth) > 1e-6)
                throw TemplateConflict("Глубина проекции");
            if (!far.IsReadOnly) far.Set(options.ProjectionDepth);
            doc.Regenerate();

            var x = HorizontalRange(segment, view.RightDirection);
            XYZ plane = new XYZ(observer.X, observer.Y, 0);
            XYZ p1 = plane + view.RightDirection * x.Min + XYZ.BasisZ * segment.Bottom;
            XYZ p2 = plane + view.RightDirection * x.Max + XYZ.BasisZ * segment.Bottom;
            XYZ p3 = plane + view.RightDirection * x.Max + XYZ.BasisZ * segment.Top;
            XYZ p4 = plane + view.RightDirection * x.Min + XYZ.BasisZ * segment.Top;
            using (var crop = view.GetCropRegionShapeManager())
            using (var loop = CurveLoop.Create(new List<Curve>
            {
                Line.CreateBound(p1, p2), Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4), Line.CreateBound(p4, p1)
            }))
                crop.SetCropShape(loop);
            doc.Regenerate();
            return view;
        }

        private (double Min, double Max) HorizontalRange(FaceSegment segment, XYZ right)
        {
            var coordinates = segment.SurfacePoints.Select(p => (p - segment.Observer).DotProduct(right)).ToList();
            double min = coordinates.Min(), max = coordinates.Max();
            if (max - min <= doc.Application.ShortCurveTolerance)
                throw new ArgumentException("Сегмент слишком короткий для построения вида.");
            return (min, max);
        }

        private static void SetInteger(View view, BuiltInParameter id, int value)
        {
            Parameter parameter = view.get_Parameter(id);
            if (parameter == null) throw new InvalidOperationException("У вида отсутствует параметр " + id + ".");
            if (parameter.AsInteger() == value) return;
            if (parameter.IsReadOnly) throw TemplateConflict(parameter.Definition.Name);
            parameter.Set(value);
        }

        private static ArgumentException TemplateConflict(string parameter) => new ArgumentException(
            "Шаблон управляет параметром «" + parameter + "» и задаёт другое значение. " +
            "Освободите этот параметр в шаблоне или выберите другой шаблон. Исходный шаблон не изменён.");

        private static Point2 ToPoint(XYZ point) => new Point2(point.X, point.Y);
        private static string SafeName(string value) => new string((value ?? "").Select(c => "\\:{}[]|;<>?`~".Contains(c) ? '_' : c).ToArray());

        private sealed class ItemFailures : IFailuresPreprocessor
        {
            public string Description { get; private set; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                var errors = accessor.GetFailureMessages().Where(f => f.GetSeverity() == FailureSeverity.Error).ToList();
                if (errors.Count == 0) return FailureProcessingResult.Continue;
                Description = string.Join("; ", errors.Select(f => f.GetDescriptionText()));
                return FailureProcessingResult.ProceedWithRollBack;
            }
        }
    }
}

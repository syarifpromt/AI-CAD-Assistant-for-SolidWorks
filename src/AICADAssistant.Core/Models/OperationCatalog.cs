using System;
using System.Collections.Generic;

namespace AICADAssistant.Core.Models
{
    public enum OperationCategory
    {
        Document,
        Sketch,
        SolidFeature,
        ReferenceGeometry
    }

    public class OperationMetadata
    {
        public OperationType Type { get; set; }
        public string Name { get; set; }
        public OperationCategory Category { get; set; }
        public string Description { get; set; }
        public List<string> RequiredParameters { get; set; }
        public List<string> OptionalParameters { get; set; }
        public bool RequiresActiveSketch { get; set; }
        public bool ProducesFeature { get; set; }
        public bool IsImplementedInExecutor { get; set; }

        public OperationMetadata(
            OperationType type,
            string name,
            OperationCategory category,
            string description,
            string[] requiredParams = null,
            string[] optionalParams = null,
            bool requiresSketch = false,
            bool producesFeature = false,
            bool isImplemented = true)
        {
            Type = type;
            Name = name;
            Category = category;
            Description = description;
            RequiredParameters = requiredParams != null ? new List<string>(requiredParams) : new List<string>();
            OptionalParameters = optionalParams != null ? new List<string>(optionalParams) : new List<string>();
            RequiresActiveSketch = requiresSketch;
            ProducesFeature = producesFeature;
            IsImplementedInExecutor = isImplemented;
        }
    }

    /// <summary>
    /// Single Source of Truth for all CAD operations, schemas, required parameters,
    /// and verified executor capabilities across AICADAssistant v2.0.
    /// </summary>
    public static class OperationCatalog
    {
        private static readonly Dictionary<OperationType, OperationMetadata> _registry =
            new Dictionary<OperationType, OperationMetadata>();

        static OperationCatalog()
        {
            // Document Operations
            Register(new OperationMetadata(OperationType.NewPart, "NewPart", OperationCategory.Document, "Membuat dokumen part baru", optionalParams: new[] { "template", "name" }));
            Register(new OperationMetadata(OperationType.ActivateDocument, "ActivateDocument", OperationCategory.Document, "Mengaktifkan dokumen model tertentu", optionalParams: new[] { "title" }));
            Register(new OperationMetadata(OperationType.Rebuild, "Rebuild", OperationCategory.Document, "Membangun ulang (rebuild) model secara paksa"));
            Register(new OperationMetadata(OperationType.SavePart, "SavePart", OperationCategory.Document, "Menyimpan dokumen part", optionalParams: new[] { "path", "filename" }));
            Register(new OperationMetadata(OperationType.ExportStep, "ExportStep", OperationCategory.Document, "Mengekspor model ke format STEP", optionalParams: new[] { "path", "filename" }));
            Register(new OperationMetadata(OperationType.ExportStl, "ExportStl", OperationCategory.Document, "Mengekspor model ke format STL", optionalParams: new[] { "path", "filename" }));

            // Sketch Operations
            Register(new OperationMetadata(OperationType.NewSketch, "NewSketch", OperationCategory.Sketch, "Membuka sketsa baru pada bidang referensi atau planar face", optionalParams: new[] { "plane", "faceNormal", "targetPoint" }));
            Register(new OperationMetadata(OperationType.EndSketch, "EndSketch", OperationCategory.Sketch, "Menutup/keluar dari mode edit sketsa aktif"));
            Register(new OperationMetadata(OperationType.AddLine, "AddLine", OperationCategory.Sketch, "Menggambar segmen garis", requiredParams: new[] { "x1", "y1", "x2", "y2" }, optionalParams: new[] { "z1", "z2" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddPolyline, "AddPolyline", OperationCategory.Sketch, "Menggambar rantai garis bersambung (polyline/poligon)", requiredParams: new[] { "points" }, optionalParams: new[] { "closed" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddRectangle, "AddRectangle", OperationCategory.Sketch, "Menggambar persegi panjang", requiredParams: new[] { "width", "height" }, optionalParams: new[] { "x", "y" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddCenterRectangle, "AddCenterRectangle", OperationCategory.Sketch, "Menggambar persegi panjang dengan titik pusat", requiredParams: new[] { "width", "height" }, optionalParams: new[] { "x", "y" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddCircle, "AddCircle", OperationCategory.Sketch, "Menggambar lingkaran", requiredParams: new[] { "diameter" }, optionalParams: new[] { "x", "y", "radius" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddArc, "AddArc", OperationCategory.Sketch, "Menggambar busur lingkaran (arc)", requiredParams: new[] { "radius" }, optionalParams: new[] { "x", "y", "startAngle", "endAngle", "xCenter", "yCenter", "xStart", "yStart", "xEnd", "yEnd", "direction" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddSlot, "AddSlot", OperationCategory.Sketch, "Menggambar slot / lubang lonjong", requiredParams: new[] { "length", "width" }, optionalParams: new[] { "x", "y", "angle" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddPolygon, "AddPolygon", OperationCategory.Sketch, "Menggambar poligon reguler", requiredParams: new[] { "sides", "radius" }, optionalParams: new[] { "x", "y" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddPoint, "AddPoint", OperationCategory.Sketch, "Menempatkan titik konstruksi", requiredParams: new[] { "x", "y" }, optionalParams: new[] { "z" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddDimension, "AddDimension", OperationCategory.Sketch, "Menambahkan dimensi kendali pada sketsa", optionalParams: new[] { "value", "entity" }, requiresSketch: true));
            Register(new OperationMetadata(OperationType.AddConstraint, "AddConstraint", OperationCategory.Sketch, "Menambahkan relasi geometris sketsa", optionalParams: new[] { "type", "entity1", "entity2" }, requiresSketch: true));

            // Solid Features
            Register(new OperationMetadata(OperationType.ExtrudeBoss, "ExtrudeBoss", OperationCategory.SolidFeature, "Mengekstrusi profil sketsa menjadi benda pejal", requiredParams: new[] { "depth" }, optionalParams: new[] { "merge", "flip", "draftAngle" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.ExtrudeCut, "ExtrudeCut", OperationCategory.SolidFeature, "Memotong benda pejal berdasarkan profil sketsa", optionalParams: new[] { "depth", "throughAll", "flip" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.RevolvedBoss, "RevolvedBoss", OperationCategory.SolidFeature, "Memutar profil sketsa 360 derajat di sekitar sumbu", optionalParams: new[] { "angle", "merge" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.RevolvedCut, "RevolvedCut", OperationCategory.SolidFeature, "Memotong benda pejal dengan memutar profil di sekitar sumbu (alur V, recess)", optionalParams: new[] { "angle" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.SweepBoss, "SweepBoss", OperationCategory.SolidFeature, "Menyapu profil di sepanjang jalur kurva", requiredParams: new[] { "profile", "path" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.Fillet, "Fillet", OperationCategory.SolidFeature, "Membulatkan sudut/tepi dengan radius tertentu", requiredParams: new[] { "radius" }, optionalParams: new[] { "edgeIndex", "targetEdge" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.Chamfer, "Chamfer", OperationCategory.SolidFeature, "Memotong sudut/tepi dengan jarak miring tertentu", requiredParams: new[] { "distance" }, optionalParams: new[] { "angle", "edgeIndex" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.SimpleHole, "SimpleHole", OperationCategory.SolidFeature, "Membuat lubang silinder sederhana", requiredParams: new[] { "diameter" }, optionalParams: new[] { "depth", "throughAll", "x", "y" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.Shell, "Shell", OperationCategory.SolidFeature, "Mengongsongkan benda pejal dengan ketebalan dinding tertentu", requiredParams: new[] { "thickness" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.Mirror, "Mirror", OperationCategory.SolidFeature, "Mencerminkan fitur terhadap bidang simetri", optionalParams: new[] { "plane", "features" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.LinearPattern, "LinearPattern", OperationCategory.SolidFeature, "Pola linear dari fitur atau benda", requiredParams: new[] { "spacing", "instances" }, optionalParams: new[] { "direction", "features" }, producesFeature: true));
            Register(new OperationMetadata(OperationType.CircularPattern, "CircularPattern", OperationCategory.SolidFeature, "Pola melingkar dari fitur (lubang baut, gigi, alur)", requiredParams: new[] { "instances" }, optionalParams: new[] { "angle", "features", "axis" }, producesFeature: true));

            // Specialized Primitives
            Register(new OperationMetadata(OperationType.CreateHemisphere, "CreateHemisphere", OperationCategory.SolidFeature, "Membuat setengah bola pejal mandiri", requiredParams: new[] { "diameter" }, optionalParams: new[] { "radius", "plane" }, producesFeature: true));

            // Reference Geometry
            Register(new OperationMetadata(OperationType.CreatePlane, "CreatePlane", OperationCategory.ReferenceGeometry, "Membuat bidang referensi baru dari offset atau referensi lain", optionalParams: new[] { "offset", "referencePlane" }, producesFeature: true));
        }

        private static void Register(OperationMetadata meta)
        {
            _registry[meta.Type] = meta;
        }

        public static OperationMetadata Get(OperationType type)
        {
            OperationMetadata meta;
            if (_registry.TryGetValue(type, out meta))
                return meta;
            return null;
        }

        public static bool IsSupported(OperationType type)
        {
            OperationMetadata meta;
            return _registry.TryGetValue(type, out meta) && meta.IsImplementedInExecutor;
        }

        public static IEnumerable<OperationMetadata> AllOperations
        {
            get { return _registry.Values; }
        }
    }
}


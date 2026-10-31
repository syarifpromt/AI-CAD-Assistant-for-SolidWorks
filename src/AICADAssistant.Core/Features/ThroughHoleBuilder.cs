using System;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Models.Contracts;

namespace AICADAssistant.Core.Features
{
    public class ThroughHoleBuilder
    {
        public static CadOperation CreateThroughHoleOperation(string id, double diameterMm, double xMm, double yMm, string planeOrFace = "Top", string parentFeature = null)
        {
            var op = new CadOperation
            {
                Id = id ?? ("hole_" + Guid.NewGuid().ToString("N").Substring(0, 8)),
                Type = OperationType.SimpleHole,
                Description = string.Format("Buat lubang tembus diameter {0:G} mm pada ({1:G}, {2:G})", diameterMm, xMm, yMm)
            };

            op.Parameters["diameter"] = diameterMm;
            op.Parameters["x"] = xMm;
            op.Parameters["y"] = yMm;
            op.Parameters["throughAll"] = true;
            op.Parameters["plane"] = planeOrFace ?? "Top";

            if (!string.IsNullOrEmpty(parentFeature))
            {
                op.DependsOn.Add(parentFeature);
            }

            return op;
        }

        public static bool ValidateHoleOperation(CadOperation op, out string error)
        {
            error = null;
            if (op == null)
            {
                error = "Operasi lubang bernilai null";
                return false;
            }

            double dia;
            if (!op.TryGetDouble("diameter", out dia) || dia <= 0.0)
            {
                error = "Diameter lubang harus bernilai angka positif";
                return false;
            }

            return true;
        }
    }
}


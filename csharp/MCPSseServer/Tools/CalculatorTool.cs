using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Data;
using System.Text.RegularExpressions;

namespace MCPSseServer.Tools;

[McpServerToolType]
public sealed class CalculatorTool
{
    [McpServerTool, Description("For mathamatical calculation, always use this tool to calculate the result of a python expression. You can use 'math' or 'random' directly, without 'import'.")]
    public static object Calculator(string python_expression)
    {
        try
        {
            // Convert Python math expressions to C# compatible expressions
            var expression = ConvertPythonToCSharp(python_expression);
            
            // For security reasons, we'll use DataTable.Compute for basic math expressions
            var table = new DataTable();
            var result = table.Compute(expression, "");
            
            return new 
            { 
                success = true, 
                result = Convert.ToDouble(result)
            };
        }
        catch (Exception ex)
        {
            return new 
            { 
                success = false, 
                error = ex.Message 
            };
        }
    }

    private static string ConvertPythonToCSharp(string pythonExpression)
    {
        var expression = pythonExpression;
        
        // Replace Python math functions with C# equivalents supported by DataTable.Compute
        expression = Regex.Replace(expression, @"math\.pi", Math.PI.ToString(), RegexOptions.IgnoreCase);
        expression = Regex.Replace(expression, @"math\.e", Math.E.ToString(), RegexOptions.IgnoreCase);
        expression = Regex.Replace(expression, @"math\.sqrt\(([^)]+)\)", "SQRT($1)", RegexOptions.IgnoreCase);
        expression = Regex.Replace(expression, @"math\.pow\(([^,]+),\s*([^)]+)\)", "POWER($1, $2)", RegexOptions.IgnoreCase);
        expression = Regex.Replace(expression, @"math\.abs\(([^)]+)\)", "ABS($1)", RegexOptions.IgnoreCase);
        
        // Handle Python's ** power operator
        expression = Regex.Replace(expression, @"(\d+(?:\.\d+)?)\s*\*\*\s*(\d+(?:\.\d+)?)", "POWER($1, $2)");
        
        // Handle Python's // integer division (convert to regular division)
        expression = Regex.Replace(expression, @"//", "/");
        
        return expression;
    }
}

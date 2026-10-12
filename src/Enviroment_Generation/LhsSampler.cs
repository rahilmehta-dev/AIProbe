using System.Data;

namespace AIprobe;

public class LhsSampler
{
    public static List<Dictionary<string, object>> PerformLhsWithDependenciesImproved(
        Dictionary<string, (double Min, double Max, string DataType, string oneOfValues, int RoundOff)>
            independentRanges,
        Dictionary<string, (string MinExpr, string MaxExpr, string DataType, string oneOfValues, int RoundOff)>
            dependentConstraints,
        int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();

        int numDimensions = independentRanges.Count;
        int numBins = Aiprobe.bin;
        var newSamples = GenerateLhsSamples(independentRanges, dependentConstraints, numBins, random);

        return newSamples;
    }
   
    public static (List<Dictionary<string, object>> InitialSamples, List<Dictionary<string, object>> FinalSamples)
        GenerateLhsSamplesForTask(
            Dictionary<string, (double Min, double Max, string DataType, string oneOfValues, int RoundOff)> independentRanges,
            Dictionary<string, (string MinExpr, string MaxExpr, string DataType, string oneOfValues, int RoundOff)> dependentConstraints,
            int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        int numBins = Aiprobe.bin;

        // Step 1: Stratified Sampling for Independent Variables
        var (initialIndependentSamples, finalIndependentSamples) = SampleIndependentVariables(independentRanges, numBins, random);

        // Step 2: Generate LHS Samples
        var lhsSamplesInitial = new List<Dictionary<string, object>>();
        var lhsSamplesFinal = new List<Dictionary<string, object>>();

        for (int i = 0; i < numBins; i++)
        {
            lhsSamplesInitial.Add(new Dictionary<string, object>(initialIndependentSamples[i]));
            lhsSamplesFinal.Add(new Dictionary<string, object>(finalIndependentSamples[i]));
        }

        // Step 3: Compute Dependent Variables
        ComputeDependentVariables(dependentConstraints, lhsSamplesInitial, lhsSamplesFinal, numBins, random);

        return (lhsSamplesInitial, lhsSamplesFinal);
    }
    
    
    private static (List<Dictionary<string, object>> InitialSamples, List<Dictionary<string, object>> FinalSamples) 
        SampleIndependentVariables(
            Dictionary<string, (double Min, double Max, string DataType, string oneOfValues, int RoundOff)> independentRanges,
            int numBins, Random random)
    {
        var stratifiedIntervalsInitial = new Dictionary<string, Queue<double>>();
        var stratifiedIntervalsFinal = new Dictionary<string, Queue<double>>();
        var stratifiedCategoricalInitial = new Dictionary<string, Queue<object>>();
        var stratifiedCategoricalFinal = new Dictionary<string, Queue<object>>();

        foreach (var (key, (minValue, maxValue, dataType, oneOfValues, roundOff)) in independentRanges)
        {
            SampleStratifiedData(key, minValue, maxValue, dataType, oneOfValues, roundOff, numBins, random, 
                stratifiedIntervalsInitial, stratifiedCategoricalInitial);
            SampleStratifiedData(key, minValue, maxValue, dataType, oneOfValues, roundOff, numBins, random, 
                stratifiedIntervalsFinal, stratifiedCategoricalFinal);
        }

        var initialSamples = GenerateLhsSamplesFromQueues(independentRanges, stratifiedIntervalsInitial, stratifiedCategoricalInitial, numBins);
        var finalSamples = GenerateLhsSamplesFromQueues(independentRanges, stratifiedIntervalsFinal, stratifiedCategoricalFinal, numBins);

        return (initialSamples, finalSamples);
    }
    
    private static List<Dictionary<string, object>> GenerateLhsSamplesFromQueues(
        Dictionary<string, (double Min, double Max, string DataType, string oneOfValues, int RoundOff)> independentRanges,
        Dictionary<string, Queue<double>> stratifiedIntervals,
        Dictionary<string, Queue<object>> stratifiedCategorical,
        int numBins)
    {
        var lhsSamples = new List<Dictionary<string, object>>();
        for (int i = 0; i < numBins; i++)
        {
            var sample = new Dictionary<string, object>();

            foreach (var (key, _) in independentRanges)
            {
                sample[key] = stratifiedCategorical.ContainsKey(key)
                    ? stratifiedCategorical[key].Dequeue()
                    : stratifiedIntervals[key].Dequeue();
            }

            lhsSamples.Add(sample);
        }

        return lhsSamples;
    }
    
    
    private static void ComputeDependentVariables(
        Dictionary<string, (string MinExpr, string MaxExpr, string DataType, string oneOfValues, int RoundOff)> dependentConstraints,
        List<Dictionary<string, object>> lhsSamplesInitial,
        List<Dictionary<string, object>> lhsSamplesFinal,
        int numBins,
        Random random)
    {
        foreach (var (key, (minExpr, maxExpr, dataType, oneOfValues, roundOff)) in dependentConstraints)
        {
            for (int i = 0; i < numBins; i++)
            {
                double minInitial = EvaluateExpression(minExpr, lhsSamplesInitial[i]);
                double maxInitial = EvaluateExpression(maxExpr, lhsSamplesInitial[i]);
                double minFinal = EvaluateExpression(minExpr, lhsSamplesFinal[i]);
                double maxFinal = EvaluateExpression(maxExpr, lhsSamplesFinal[i]);

                if (minInitial > maxInitial || minFinal > maxFinal)
                    throw new ArgumentException($"Invalid range for dependent variable '{key}'");

                lhsSamplesInitial[i][key] = SampleDependentData(minInitial, maxInitial, dataType, oneOfValues, roundOff, maxInitial, random);
                lhsSamplesFinal[i][key] = SampleDependentData(minFinal, maxFinal, dataType, oneOfValues, roundOff, maxFinal, random);
            }
        }
    }


    private static double EvaluateExpression(string expression, Dictionary<string, double> context)
    {
        foreach (var kvp in context)
        {
            expression = expression.Replace(kvp.Key, kvp.Value.ToString());
        }

        var dataTable = new DataTable();
        return Convert.ToDouble(dataTable.Compute(expression, null));
    }


    private static double EvaluateExpression(string expression, Dictionary<string, object> context)
    {
        foreach (var kvp in context)
        {
            string replacementValue;

            if (kvp.Value is double numericValue)
            {
                replacementValue = numericValue.ToString(); 
            }
            else if (kvp.Value is List<int> arrayValue)
            {
                replacementValue = arrayValue.ToString(); 
            }
            else
            {
                throw new ArgumentException($"Unsupported data type for key '{kvp.Key}' in expression evaluation.");
            }

            expression = expression.Replace(kvp.Key, replacementValue);
        }

        var dataTable = new DataTable();
        return Convert.ToDouble(dataTable.Compute(expression, null));
    }

    private static IEnumerable<string> ExtractUnresolvedKeys(string expression)
    {
        var unresolvedKeys = new List<string>();
        int startIndex = expression.IndexOf('{');
        while (startIndex != -1)
        {
            int endIndex = expression.IndexOf('}', startIndex);
            if (endIndex != -1)
            {
                var key = expression.Substring(startIndex + 1, endIndex - startIndex - 1);
                unresolvedKeys.Add(key);
                startIndex = expression.IndexOf('{', endIndex);
            }
            else
            {
                break; // No matching closing brace
            }
        }

        return unresolvedKeys;
    }


    private static List<Dictionary<string, object>> GenerateLhsSamples(
        Dictionary<string, (double Min, double Max, string DataType, string oneOfValues, int RoundOff)>
            independentRanges,
        Dictionary<string, (string MinExpr, string MaxExpr, string DataType, string oneOfValues, int RoundOff)>
            dependentConstraints,
        int bins,
        Random random)
    {
        var stratifiedIntervals = new Dictionary<string, Queue<double>>();
        var stratifiedCategorical = new Dictionary<string, Queue<object>>();

        // Step 1: Stratified Sampling for Independent Variables
        foreach (var (key, (minValue, maxValue, dataType, oneOfValues, roundOff)) in independentRanges)
        {
            SampleStratifiedData(key, minValue, maxValue, dataType, oneOfValues, roundOff, bins, random,
                stratifiedIntervals, stratifiedCategorical);
        }

        // Step 2: Generate LHS Samples
        var lhsSamples = new List<Dictionary<string, object>>();
        for (int i = 0; i < bins; i++)
        {
            var sample = new Dictionary<string, object>();

            // Assign Independent Variables
            foreach (var (key, (_, _, dataType, _, _)) in independentRanges)
            {
                if (stratifiedCategorical.ContainsKey(key))
                    sample[key] = stratifiedCategorical[key].Dequeue();
                else
                    sample[key] = stratifiedIntervals[key].Dequeue();
            }

            // Step 3: Compute Dependent Variables
            foreach (var (key, (minExpr, maxExpr, dataType, oneOfValues, roundOff)) in dependentConstraints)
            {
                double minValue = EvaluateExpression(minExpr, sample);
                double maxValue = EvaluateExpression(maxExpr, sample);

                // Sample stratified dependent variable values
                object dependentValue = SampleDependentData(minValue, maxValue, dataType, oneOfValues, roundOff,
                    maxValue, random);
                sample[key] = dependentValue;
            }

            lhsSamples.Add(sample);
        }

        return lhsSamples;
    }

    internal static void SampleStratifiedData(string key, double minValue, double maxValue, string dataType,
        string oneOfValues, int roundOff, int bins, Random random,
        Dictionary<string, Queue<double>> stratifiedIntervals, Dictionary<string, Queue<object>> stratifiedCategorical,
        Dictionary<string, object> currentSample = null)
    {
        if (dataType.Equals("list", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("array", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("tuple", StringComparison.OrdinalIgnoreCase))
        {
            var possibleValues = oneOfValues.Split(',').Select(v => v.Trim()).ToList();
            var cumulativeDistribution = GenerateUniformCDF(possibleValues.Count);

            var stratifiedSamples = new List<object>();

            for (int i = 0; i < bins; i++)
            {
                object sampledData;

                if (minValue == maxValue)
                {
                    int index = SampleFromCDF(cumulativeDistribution, random);
                    sampledData = possibleValues[index];
                }
                else
                {
                    int sampleSize = random.Next((int)minValue, (int)maxValue + 1);
                    var sampledItems = new List<object>();

                    for (int j = 0; j < sampleSize; j++)
                    {
                        int index = SampleFromCDF(cumulativeDistribution, random);
                        sampledItems.Add(possibleValues[index]);
                    }

                    sampledData = dataType switch
                    {
                        "list" => $"[{string.Join(", ", sampledItems)}]", // Convert list to string format
                        "array" => $"[{string.Join(", ", sampledItems)}]",
                        "tuple" => $"[{string.Join(", ", sampledItems)}]",
                        _ => sampledItems
                    };
                }

                stratifiedSamples.Add(sampledData);
            }

            stratifiedSamples = stratifiedSamples.OrderBy(_ => random.Next()).ToList();
            stratifiedCategorical[key] = new Queue<object>(stratifiedSamples);
        }
        else
        {
            double binWidth = (maxValue - minValue) / bins;
            var intervals = new List<double>();
            List<int> binIndices = Enumerable.Range(0, bins).OrderBy(_ => random.Next()).ToList();

            for (int i = 0; i < bins; i++)
            {
                double binStart = minValue + binIndices[i] * binWidth;
                double sampledValue = binStart + random.NextDouble() * binWidth;
                sampledValue = Math.Min(sampledValue, maxValue);

                if (dataType.Equals("int", StringComparison.OrdinalIgnoreCase))
                    sampledValue = Math.Round(sampledValue);
                else if (dataType.Equals("float", StringComparison.OrdinalIgnoreCase))
                    sampledValue = Math.Round(sampledValue, 6);
                else if (dataType.Equals("double", StringComparison.OrdinalIgnoreCase))
                    sampledValue = Math.Round(sampledValue, 6);

                intervals.Add(sampledValue);
            }

            stratifiedIntervals[key] = new Queue<double>(intervals);
        }
    }


    internal static object SampleDependentData(double minValue, double maxValue, string dataType, string oneOfValues,
        int roundOff, double sample, Random random)
    {
        if (dataType.Equals("list", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("array", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("tuple", StringComparison.OrdinalIgnoreCase))
        {
            var possibleValues = oneOfValues.Split(',').Select(v => v.Trim()).ToList();
            var cumulativeDistribution = GenerateUniformCDF(possibleValues.Count);

            object sampledData;
            if (minValue == maxValue)
            {
                int index = SampleFromCDF(cumulativeDistribution, random);
                sampledData = possibleValues[index];
            }
            else
            {
                int sampleSize = random.Next((int)minValue, (int)maxValue + 1);
                var sampledItems = new List<object>();

                for (int j = 0; j < maxValue; j++)
                {
                    int index = SampleFromCDF(cumulativeDistribution, random);
                    sampledItems.Add(possibleValues[index]);
                }

                sampledData = dataType switch
                {
                    "list" => $"[{string.Join(", ", sampledItems)}]", // Convert list to string format
                    "array" => $"[{string.Join(", ", sampledItems)}]",
                    "tuple" => $"[{string.Join(", ", sampledItems)}]",
                    _ => sampledItems
                };
            }

            return sampledData;
        }
        else
        {
            double sampledValue = minValue + random.NextDouble() * (maxValue - minValue);
            sampledValue = Math.Min(sampledValue, maxValue);

            if (dataType.Equals("int", StringComparison.OrdinalIgnoreCase))
                return Math.Round(sampledValue);
            else if (dataType.Equals("float", StringComparison.OrdinalIgnoreCase))
                return Math.Round(sampledValue, 6);

            return sampledValue;
        }
    }


    private static List<double> GenerateUniformCDF(int size)
    {
        var cdf = new List<double>();
        for (int i = 0; i < size ; i++)
        {
            cdf.Add((i + 1) / (double)size);
        }

        return cdf;
    }

    private static int SampleFromCDF(List<double> cdf, Random random)
    {
        double sample = random.NextDouble();
        for (int i = 0; i < cdf.Count; i++)
        {
            if (sample <= cdf[i])
            {
                return i;
            }
        }

        return cdf.Count - 1;
    }
    
    
     // public static (List<Dictionary<string, object>> InitialSamples, List<Dictionary<string, object>> FinalSamples)
    //     GenerateLhsSamplesForTask(
    //         Dictionary<string, (double Min, double Max, string DataType,string  oneOfValues, int RoundOff)>
    //             independentRanges,
    //         Dictionary<string, (string MinExpr, string MaxExpr, string DataType, string oneOfValues, int RoundOff)>
    //             dependentConstraints, int? seed = null)
    // {
    //     var random = seed.HasValue ? new Random(seed.Value) : new Random();
    //     int numBins = Aiprobe.bin;
    //     var stratifiedIntervalsInitial = new Dictionary<string, Queue<double>>();
    //     var stratifiedIntervalsFinal = new Dictionary<string, Queue<double>>();
    //     var stratifiedCategoricalInitial = new Dictionary<string, Queue<object>>();
    //     var stratifiedCategoricalFinal = new Dictionary<string, Queue<object>>();
    //
    //     // Step 1: Stratified Sampling for Independent Variables
    //     foreach (var (key, (minValue, maxValue, dataType, oneOfValues, roundOff)) in independentRanges)
    //     {
    //         SampleStratifiedData(key, minValue, maxValue, dataType, oneOfValues, roundOff, numBins, random, stratifiedIntervalsInitial, stratifiedCategoricalInitial);
    //
    //         SampleStratifiedData(
    //             key, minValue, maxValue, dataType, oneOfValues, roundOff,
    //             numBins, random, stratifiedIntervalsFinal, stratifiedCategoricalFinal);
    //     }
    //
    //     // Step 2: Generate LHS Samples
    //     var lhsSamplesInitial = new List<Dictionary<string, object>>();
    //     var lhsSamplesFinal = new List<Dictionary<string, object>>();
    //
    //     for (int i = 0; i < numBins; i++)
    //     {
    //         var sampleInitial = new Dictionary<string, object>();
    //         var sampleFinal = new Dictionary<string, object>();
    //
    //         // Assign Independent Variables
    //         foreach (var (key, (_, _, dataType, _, _)) in independentRanges)
    //         {
    //             if (stratifiedCategoricalInitial.ContainsKey(key))
    //                 sampleInitial[key] = stratifiedCategoricalInitial[key].Dequeue();
    //             else
    //                 sampleInitial[key] = stratifiedIntervalsInitial[key].Dequeue();
    //
    //             if (stratifiedCategoricalFinal.ContainsKey(key))
    //                 sampleFinal[key] = stratifiedCategoricalFinal[key].Dequeue();
    //             else
    //                 sampleFinal[key] = stratifiedIntervalsFinal[key].Dequeue();
    //         }
    //
    //         lhsSamplesInitial.Add(sampleInitial);
    //         lhsSamplesFinal.Add(sampleFinal);
    //     }
    //
    //     // Step 3: Compute Dependent Variables
    //     foreach (var (key, (minExpr, maxExpr, dataType, oneOfValues, roundOff)) in dependentConstraints)
    //     {
    //         var dependentValuesInitial = new Queue<object>();
    //         var dependentValuesFinal = new Queue<object>();
    //
    //         for (int i = 0; i < numBins; i++)
    //         {
    //             var sampleInitial = lhsSamplesInitial[i];
    //             var sampleFinal = lhsSamplesFinal[i];
    //
    //             double minInitial = EvaluateExpression(minExpr, sampleInitial);
    //             double maxInitial = EvaluateExpression(maxExpr, sampleInitial);
    //             double minFinal = EvaluateExpression(minExpr, sampleFinal);
    //             double maxFinal = EvaluateExpression(maxExpr, sampleFinal);
    //             
    //
    //             if (minInitial > maxInitial || minFinal > maxFinal)
    //                 throw new ArgumentException($"Invalid range for dependent variable '{key}'");
    //
    //             object dependentValueInitial =
    //                 SampleDependentData(minInitial, maxInitial, dataType, oneOfValues, roundOff,maxInitial ,random);
    //             object dependentValueFinal =
    //                 SampleDependentData(minFinal, maxFinal, dataType, oneOfValues, roundOff, maxFinal,random);
    //
    //             dependentValuesInitial.Enqueue(dependentValueInitial);
    //             dependentValuesFinal.Enqueue(dependentValueFinal);
    //         }
    //
    //         for (int i = 0; i < numBins; i++)
    //         {
    //             lhsSamplesInitial[i][key] = dependentValuesInitial.Dequeue();
    //             lhsSamplesFinal[i][key] = dependentValuesFinal.Dequeue();
    //         }
    //     }
    //
    //     return (lhsSamplesInitial, lhsSamplesFinal);
    // }
    
}
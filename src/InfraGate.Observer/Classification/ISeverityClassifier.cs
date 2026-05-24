namespace InfraGate.Observer.Classification;

internal interface ISeverityClassifier
{
    (Severity Severity, string MatchedRule) Classify(AnomalyEvidence evidence);
}

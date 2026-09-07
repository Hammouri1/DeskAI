using DeskAI.Core.Files;
using FileClassification = DeskAI.Core.Classification.Classification;

namespace DeskAI.Core.Abstractions;

public interface IFileClassifier
{
    FileClassification Classify(FileItem file);
}

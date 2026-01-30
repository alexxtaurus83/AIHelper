namespace AIHelper.Interfaces
{
    public interface IGitProvider
    {
        Task<string> GetFileContentAsync(string repoId, string filePath, string branch, string token);
        Task CreateBranchAsync(string repoId, string source, string newBranch, string token);
        Task CommitFileAsync(string repoId, string branch, string filePath, string content, string message, string token);
        Task CreateMergeRequestAsync(string repoId, string source, string target, string title, string token);
    }
}
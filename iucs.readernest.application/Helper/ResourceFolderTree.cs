namespace iucs.readernest.application.Helper
{
    /// <summary>Walks the (small) folder tree in memory; folders are few, so one load beats recursive queries.</summary>
    public static class ResourceFolderTree
    {
        /// <summary>The given folders plus every folder beneath them.</summary>
        public static HashSet<Guid> WithDescendants(IReadOnlyCollection<(Guid Id, Guid? ParentId)> all, IEnumerable<Guid> roots)
        {
            var result = new HashSet<Guid>(roots);
            var queue = new Queue<Guid>(result);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in all.Where(f => f.ParentId == current))
                {
                    if (result.Add(child.Id))
                    {
                        queue.Enqueue(child.Id);
                    }
                }
            }

            return result;
        }
    }
}

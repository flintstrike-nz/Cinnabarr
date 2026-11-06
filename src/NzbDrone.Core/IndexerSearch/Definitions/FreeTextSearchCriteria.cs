using System.Collections.Generic;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    // A search for whatever the user typed, not tied to a series or movie in the library.
    public class FreeTextSearchCriteria : SearchCriteriaBase
    {
        public string Query { get; set; }

        public FreeTextSearchCriteria()
        {
            SceneTitles = new List<string>();
            UserInvokedSearch = true;
            InteractiveSearch = true;
        }

        public override string ToString()
        {
            return $"[{Query}]";
        }
    }
}

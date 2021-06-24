﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Rulesets;
using osu.Game.Resources.Localisation.Web;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.BeatmapListing
{
    public class BeatmapListingFilterControl : CompositeDrawable
    {
        /// <summary>
        /// Fired when a search finishes.
        /// SearchFinished.Type = ResultsReturned when results returned. Contains only new items in the case of pagination.
        /// SearchFinished.Type = SupporterOnlyFilter when a non-supporter user applied supporter-only filters.
        /// </summary>
        public Action<SearchResult> SearchFinished;

        /// <summary>
        /// Fired when search criteria change.
        /// </summary>
        public Action SearchStarted;

        /// <summary>
        /// Any time the search text box receives key events (even while masked).
        /// </summary>
        public Action TypingStarted;

        /// <summary>
        /// True when pagination has reached the end of available results.
        /// </summary>
        private bool noMoreResults;

        /// <summary>
        /// The current page fetched of results (zero index).
        /// </summary>
        public int CurrentPage { get; private set; }

        private readonly BeatmapListingSearchControl searchControl;
        private readonly BeatmapListingSortTabControl sortControl;
        private readonly Box sortControlBackground;

        private ScheduledDelegate queryChangedDebounce;

        private SearchBeatmapSetsRequest getSetsRequest;
        private SearchBeatmapSetsResponse lastResponse;

        [Resolved]
        private IAPIProvider api { get; set; }

        [Resolved]
        private RulesetStore rulesets { get; set; }

        public BeatmapListingFilterControl()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Masking = true,
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Colour = Color4.Black.Opacity(0.25f),
                            Type = EdgeEffectType.Shadow,
                            Radius = 3,
                            Offset = new Vector2(0f, 1f),
                        },
                        Child = searchControl = new BeatmapListingSearchControl
                        {
                            TypingStarted = () => TypingStarted?.Invoke()
                        }
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 40,
                        Children = new Drawable[]
                        {
                            sortControlBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both
                            },
                            sortControl = new BeatmapListingSortTabControl
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Margin = new MarginPadding { Left = 20 }
                            }
                        }
                    }
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            sortControlBackground.Colour = colourProvider.Background5;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            var sortCriteria = sortControl.Current;
            var sortDirection = sortControl.SortDirection;

            searchControl.Query.BindValueChanged(query =>
            {
                sortCriteria.Value = string.IsNullOrEmpty(query.NewValue) ? SortCriteria.Ranked : SortCriteria.Relevance;
                sortDirection.Value = SortDirection.Descending;
                queueUpdateSearch(true);
            });

            searchControl.General.CollectionChanged += (_, __) => queueUpdateSearch();
            searchControl.Ruleset.BindValueChanged(_ => queueUpdateSearch());
            searchControl.Category.BindValueChanged(_ => queueUpdateSearch());
            searchControl.Genre.BindValueChanged(_ => queueUpdateSearch());
            searchControl.Language.BindValueChanged(_ => queueUpdateSearch());
            searchControl.Extra.CollectionChanged += (_, __) => queueUpdateSearch();
            searchControl.Ranks.CollectionChanged += (_, __) => queueUpdateSearch();
            searchControl.Played.BindValueChanged(_ => queueUpdateSearch());
            searchControl.ExplicitContent.BindValueChanged(_ => queueUpdateSearch());

            sortCriteria.BindValueChanged(_ => queueUpdateSearch());
            sortDirection.BindValueChanged(_ => queueUpdateSearch());
        }

        public void TakeFocus() => searchControl.TakeFocus();

        /// <summary>
        /// Fetch the next page of results. May result in a no-op if a fetch is already in progress, or if there are no results left.
        /// </summary>
        public void FetchNextPage()
        {
            // there may be no results left.
            if (noMoreResults)
                return;

            // there may already be an active request.
            if (getSetsRequest != null)
                return;

            if (lastResponse != null)
                CurrentPage++;

            performRequest();
        }

        private void queueUpdateSearch(bool queryTextChanged = false)
        {
            SearchStarted?.Invoke();

            resetSearch();

            queryChangedDebounce = Scheduler.AddDelayed(() =>
            {
                resetSearch();
                FetchNextPage();
            }, queryTextChanged ? 500 : 100);
        }

        private void performRequest()
        {
            getSetsRequest = new SearchBeatmapSetsRequest(
                searchControl.Query.Value,
                searchControl.Ruleset.Value,
                lastResponse?.Cursor,
                searchControl.General,
                searchControl.Category.Value,
                sortControl.Current.Value,
                sortControl.SortDirection.Value,
                searchControl.Genre.Value,
                searchControl.Language.Value,
                searchControl.Extra,
                searchControl.Ranks,
                searchControl.Played.Value,
                searchControl.ExplicitContent.Value);

            getSetsRequest.Success += response =>
            {
                var sets = response.BeatmapSets.Select(responseJson => responseJson.ToBeatmapSet(rulesets)).ToList();

                if (sets.Count == 0)
                    noMoreResults = true;

                if (CurrentPage == 0)
                    searchControl.BeatmapSet = sets.FirstOrDefault();

                lastResponse = response;
                getSetsRequest = null;

                // check if an non-supporter user used supporter-only filters
                if (!api.LocalUser.Value.IsSupporter)
                {
                    List<LocalisableString> filters = new List<LocalisableString>();

                    if (searchControl.Played.Value != SearchPlayed.Any)
                        filters.Add(BeatmapsStrings.ListingSearchFiltersPlayed);

                    if (searchControl.Ranks.Any())
                        filters.Add(BeatmapsStrings.ListingSearchFiltersRank);

                    if (filters.Any())
                    {
                        SearchFinished?.Invoke(SearchResult.SupporterOnlyFilter(filters));
                        return;
                    }
                }

                SearchFinished?.Invoke(SearchResult.ResultsReturned(sets));
            };

            api.Queue(getSetsRequest);
        }

        private void resetSearch()
        {
            noMoreResults = false;
            CurrentPage = 0;

            lastResponse = null;

            getSetsRequest?.Cancel();
            getSetsRequest = null;

            queryChangedDebounce?.Cancel();
        }

        protected override void Dispose(bool isDisposing)
        {
            resetSearch();

            base.Dispose(isDisposing);
        }

        public enum SearchResultType
        {
            // returned with Results
            ResultsReturned,
            // non-supporter user applied supporter-only filters
            SupporterOnlyFilter
        }

        // Results only valid when Type == ResultsReturned
        // Filters only valid when Type == SupporterOnlyFilter
        public struct SearchResult
        {
            public SearchResultType Type { get; private set; }
            public List<BeatmapSetInfo> Results { get; private set; }
            public List<LocalisableString> Filters { get; private set; }

            public static SearchResult ResultsReturned(List<BeatmapSetInfo> results) => new SearchResult
            {
                Type = SearchResultType.ResultsReturned,
                Results = results
            };

            public static SearchResult SupporterOnlyFilter(List<LocalisableString> filters) => new SearchResult
            {
                Type = SearchResultType.SupporterOnlyFilter,
                Filters = filters
            };
        }
    }
}

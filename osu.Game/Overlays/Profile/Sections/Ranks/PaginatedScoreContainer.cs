﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Containers;
using osu.Game.Online.API.Requests;
using osu.Game.Users;
using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Online.API.Requests.Responses;
using System.Collections.Generic;
using osu.Game.Online.API;
using osu.Framework.Allocation;
using osu.Framework.Localisation;

namespace osu.Game.Overlays.Profile.Sections.Ranks
{
    public class PaginatedScoreContainer : PaginatedProfileSubsection<APIScoreInfo>
    {
        private readonly ScoreType type;

        public PaginatedScoreContainer(ScoreType type, Bindable<User> user, LocalisableString headerText)
            : base(user, headerText)
        {
            this.type = type;

            ItemsPerPage = 5;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            ItemsContainer.Direction = FillDirection.Vertical;
        }

        protected override int GetCount(User user)
        {
            switch (type)
            {
                case ScoreType.Best:
                    return user.ScoresBestCount;

                case ScoreType.Firsts:
                    return user.ScoresFirstCount;

                case ScoreType.Recent:
                    return user.ScoresRecentCount;

                default:
                    return 0;
            }
        }

        protected override void OnItemsReceived(List<APIScoreInfo> items)
        {
            if (VisiblePages == 0)
                drawableItemIndex = 0;

            base.OnItemsReceived(items);
        }

        protected override APIRequest<List<APIScoreInfo>> CreateRequest() =>
            new GetUserScoresRequest(User.Value.Id, type, VisiblePages++, ItemsPerPage);

        private int drawableItemIndex;

        protected override Drawable CreateDrawableItem(APIScoreInfo model)
        {
            switch (type)
            {
                default:
                    return new DrawableProfileScore(model);

                case ScoreType.Best:
                    return new DrawableProfileWeightedScore(model, Math.Pow(0.95, drawableItemIndex++));
            }
        }
    }
}

﻿using System;

namespace Keeper.BacktraQ
{
    public class PassthroughQuery
        : Query
    {
        private readonly Func<Query> subQuery;

        public PassthroughQuery(Func<Query> subQuery)
        {
            this.subQuery = subQuery;
        }

        public override QueryResult Run()
        {
            return this.InvokeAsPassthrough(this.subQuery());
        }
    }
}

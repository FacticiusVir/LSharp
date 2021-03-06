﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Keeper.BacktraQ
{
    public class Query
        : IEnumerable
    {
        private readonly Func<QueryResult> run;

        internal Query(Func<QueryResult> run) => this.run = run;

        internal QueryResult Run() => this.run();

        public Query FindAll<T>(Var<T> variable, Var<VarList<T>> results) => Wrap(() => results.Unify(this.AsVarList(variable)));

        public Func<VarList<T>, Query> FindAll<T>(Var<T> variable) => results => Wrap(() => results <= this.AsVarList(variable));

        IEnumerator IEnumerable.GetEnumerator() => this.AsEnumerable().GetEnumerator();

        public static Query Do(Action action) => new Query(() =>
        {
            action();

            return QueryResult.Success;
        });

        public static Query Do(Action action, Action rollback) => Do(action) | (Do(rollback) & Fail);

        public static Query When(Func<bool> predicate) => new Query(() => predicate() ? QueryResult.Success : QueryResult.Fail);

        public static Query Wrap(Func<Query> query) => new Query(() => query().Run());

        public static Query IfThen(Query condition, Query action) => IfThen(condition, action, Success);

        public static Query IfThen(Query condition, Query action, Query elseAction) => Wrap(() => condition.Succeeds() ? action : elseAction);

        public static Query Map<T, V>(Var<T> left, Var<V> right, Func<T, V> map, Func<V, T> unmap = null)
        {
            return Wrap(() =>
            {
                if (left.HasValue)
                {
                    return right <= map(left.Value);
                }
                else if (right.HasValue && unmap != null)
                {
                    return left <= unmap(right.Value);
                }
                else
                {
                    throw new Exception("Insufficiently instantiated terms.");
                }
            });
        }

        public static Query Map<T, V, W>(Var<T> left, Var<V> right, Var<W> result, Func<T, V, W> map, Func<V, W, T> unmapLeft, Func<T, W, V> unmapRight)
        {
            return Wrap(() =>
            {
                if (left.HasValue & right.HasValue)
                {
                    return result <= map(left.Value, right.Value);
                }
                else if (right.HasValue && result.HasValue)
                {
                    return left <= unmapLeft(right.Value, result.Value);
                }
                else if (left.HasValue && result.HasValue)
                {
                    return right <= unmapRight(left.Value, result.Value);
                }
                else
                {
                    throw new Exception("Insufficiently instantiated terms.");
                }
            });
        }

        public static Query Map<T, V, W>(out Var<T> t, out Var<V> v, Var<W> w, params (T, V, W)[] mappings) => Map(NewVar(out t), NewVar(out v), w, mappings);

        public static Query Map<T, V, W>(Var<T> t, Var<V> v, Var<W> w, params (T, V, W)[] mappings)
        {
            return mappings.Select(mapping => t <= mapping.Item1
                                                & v <= mapping.Item2
                                                & w <= mapping.Item3).Any();
        }

        public static Query Construct<T, V, W>(out Var<T> left, out Var<V> right, Var<W> result, Func<T, V, W> construct, Func<W, (T, V)> deconstruct = null) => Construct(NewVar(out left), NewVar(out right), result, construct, deconstruct);

        public static Query Construct<T, V, W>(Var<T> left, Var<V> right, Var<W> result, Func<T, V, W> construct, Func<W, (T, V)> deconstruct = null)
        {
            return Wrap(() =>
            {
                if (left.HasValue & right.HasValue)
                {
                    return result <= construct(left.Value, right.Value);
                }
                else if (result.HasValue && deconstruct != null)
                {
                    var values = deconstruct(result.Value);

                    return left <= values.Item1
                            & right <= values.Item2;
                }
                else
                {
                    throw new Exception("Insufficiently instantiated terms.");
                }
            });
        }

        public static Var<T> NewVar<T>(out Var<T> variable) => variable = new Var<T>();
        
        public static Var<(Var<T>, Var<U>)> NewVar<T, U>(out Var<(Var<T>, Var<U>)> variable) => variable = new Var<(Var<T>, Var<U>)>();

        public static Func<Query> NewVar<T>(Func<Var<T>, Query> query, out Var<T> variable)
        {
            variable = new Var<T>();

            var liftedVariable = variable;

            return () => query(liftedVariable);
        }

        public static VarList<T> NewList<T>(out VarList<T> list) => list = new VarList<T>();

        public static VarList<(Var<T>, Var<U>)> NewList<T, U>(out VarList<(Var<T>, Var<U>)> list) => list = new VarList<(Var<T>, Var<U>)>();

        public static Query Success
        {
            get;
        } = new Query(() => QueryResult.Success);

        public static Query Fail
        {
            get;
        } = new Query(() => QueryResult.Fail);

        private static readonly Random rnd = new Random();

        public static Query Random(Var<int> bound, out Var<int> value) => Random(bound, NewVar(out value));

        public static Query Random(Var<int> bound, Var<int> value)
            => When(() => bound.HasValue)
                & (() =>
                    {
                        var sequence = Enumerable.Range(0, bound.Value).ToList();

                        Shuffle(sequence);

                        return value <= VarList.Create<int>(sequence).Member;
                    });

        private static void Shuffle<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rnd.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
        public static Query All(params Query[] goals) => goals.All();

        public static Query Any(params Query[] options) => options.Any();

        public static Query Not(Query query) => When(() => !query.Succeeds(true));

        public static Query operator &(Query left, Query right) => left.And(right);

        public static Query operator &(Query left, Func<Query> right) => left.And(right);

        public static Query operator |(Query left, Query right) => left.Or(right);

        public static Query operator |(Query left, Func<Query> right) => left.Or(right);

        public static Query operator !(Query query) => Not(query);

        public static Query operator %(Query condition, Query action) => IfThen(condition, action);

        public static implicit operator Query(Func<Query> func) => Wrap(func);
    }

    public static class QueryExtensions
    {
        public static Query All(this IEnumerable<Query> goals) => goals.Aggregate((x, y) => x.And(y));

        public static Query Any(this IEnumerable<Query> options) => options.Aggregate((x, y) => x.Or(y));

        public static Func<T, Query> Any<T>(this IEnumerable<Func<T, Query>> options) => a => options.Select(option => option(a)).Aggregate((x, y) => x.Or(y));

        public static Func<T, V, Query> Any<T, V>(this IEnumerable<Func<T, V, Query>> options) => (a, b) => options.Select(option => option(a, b)).Aggregate((x, y) => x.Or(y));

        public static Query Chain<T>(this Func<VarList<T>, Var<T>, Query> subQuery, int repetitions, VarList<T> input, VarList<T> output = null)
        {
            output ??= new VarList<T>();

            var queryAccumulator = Query.Success;
            VarList<T> intermediary = null;

            for (int index = 0; index < repetitions; index++)
            {
                intermediary = new VarList<T>();
                var element = new Var<T>();

                queryAccumulator &= subQuery(input, element) & intermediary <= input.Append(element);
            }

            return intermediary <= output & queryAccumulator;
        }

        public static Query Chain<T>(this Func<Var<T>, Var<T>, Query> subQuery, int repetitions, Var<T> input, Var<T> output = null)
        {
            if (repetitions == 0)
            {
                return Query.Success;
            }

            output ??= new Var<T>();

            var intermediary = new Var<T>();

            var queryAccumulator = subQuery(input, intermediary);

            for (int index = 1; index < repetitions; index++)
            {
                input = intermediary;
                intermediary = new Var<T>();

                queryAccumulator &= subQuery(input, intermediary);
            }

            return intermediary <= output & queryAccumulator;
        }

        public static bool Succeeds(this Query query, bool revertAll = false)
        {
            foreach (var result in query.AsEnumerable(revertAll))
            {
                return true;
            }

            return false;
        }

        public static Var<VarList<T>> AsVarList<T>(this Query query, Var<T> resultVariable)
        {
            var resultList = new List<Var<T>>();

            foreach (var result in query)
            {
                if (resultVariable.HasValue)
                {
                    resultList.Add(resultVariable.Value);
                }
                else if (resultVariable.IsBound)
                {
                    resultList.Add(resultVariable.Dereference());
                }
                else
                {
                    resultList.Add(new Var<T>());
                }
            }

            return VarList.Create(resultList.ToArray());
        }

        public static IEnumerable<T> AsEnumerable<T>(this Query query, Var<T> resultVariable)
        {
            foreach (var result in query)
            {
                yield return resultVariable.Value;
            }
        }

        public static IEnumerable AsEnumerable(this Query query, bool revertAll = false)
        {
            try
            {
                Trail.Enter();

                var nextQuery = query;

                while (nextQuery != null)
                {
                    var currentQuery = nextQuery;
                    var result = currentQuery.Run();

                    switch (result.Type)
                    {
                        case QueryResultType.ChoicePoint:
                            nextQuery = result.Continuation;
                            Trail.Current.ChoicePoint(result.Alternate);
                            break;
                        case QueryResultType.Success:
                            yield return null;
                            nextQuery = Trail.Current.Backtrack();
                            break;
                        default:
                            nextQuery = Trail.Current.Backtrack();
                            break;
                    }
                }
            }
            finally
            {
                Trail.Exit(revertAll);
            }
        }
    }
}

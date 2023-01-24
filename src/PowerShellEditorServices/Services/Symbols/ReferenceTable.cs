// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Management.Automation.Language;
using Microsoft.PowerShell.EditorServices.Services.TextDocument;
using Microsoft.PowerShell.EditorServices.Services.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.PowerShell.EditorServices.Services;

/// <summary>
/// Represents the symbols that are referenced and their locations within a single document.
/// </summary>
internal sealed class ReferenceTable
{
    private readonly ScriptFile _parent;

    private readonly ConcurrentDictionary<string, ConcurrentBag<SymbolReference>> _symbolReferences = new(StringComparer.OrdinalIgnoreCase);

    private bool _isInited;

    public ReferenceTable(ScriptFile parent) => _parent = parent;

    /// <summary>
    /// Clears the reference table causing it to re-scan the source AST when queried.
    /// </summary>
    public void TagAsChanged()
    {
        _symbolReferences.Clear();
        _isInited = false;
    }

    /// <summary>
    /// Prefer checking if the dictionary has contents to determine if initialized. The field
    /// `_isInited` is to guard against re-scanning files with no command references, but will
    /// generally be less reliable of a check.
    /// </summary>
    private bool IsInitialized => !_symbolReferences.IsEmpty || _isInited;

    internal bool TryGetReferences(string command, out ConcurrentBag<SymbolReference>? references)
    {
        EnsureInitialized();
        return _symbolReferences.TryGetValue(command, out references);
    }

    internal SymbolReference? TryGetSymbolAtPosition(int line, int column) => GetAllReferences()
        .FirstOrDefault((i) => i.NameRegion.ContainsPosition(line, column));

    internal IEnumerable<SymbolReference> GetAllReferences()
    {
        EnsureInitialized();
        foreach (ConcurrentBag<SymbolReference> bag in _symbolReferences.Values)
        {
            foreach (SymbolReference symbol in bag)
            {
                yield return symbol;
            }
        }
    }

    internal void EnsureInitialized()
    {
        if (IsInitialized)
        {
            return;
        }

        _parent.ScriptAst.Visit(new SymbolVisitor(_parent, AddReference));
    }

    private AstVisitAction AddReference(SymbolReference symbol)
    {
        // We have to exclude implicit things like `$this` that don't actually exist.
        if (symbol.ScriptRegion.IsEmpty())
        {
            return AstVisitAction.Continue;
        }

        _symbolReferences.AddOrUpdate(
            symbol.SymbolName,
            _ => new ConcurrentBag<SymbolReference> { symbol },
            (_, existing) =>
            {
                existing.Add(symbol);
                return existing;
            });

        return AstVisitAction.Continue;
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace GitCredentialManager.Tty;

public static class TerminalPrompts
{
    public static SelectionPrompt<SelectionPromptItem<T>> CreateSelection<T>()
    {
        return new SelectionPrompt<SelectionPromptItem<T>>()
            .UseConverter(x => x.Label)
            .AddCancelResult(() => throw new OperationCanceledException("User cancelled the prompt"));
    }

    extension<T>(IPrompt<T> prompt)
    {
        public Task<T> ShowAsync(IAnsiConsole console) => prompt.ShowAsync(console, CancellationToken.None);
    }

    extension<T> (SelectionPrompt<SelectionPromptItem<T>> prompt)
    {
        public ISelectionItem<SelectionPromptItem<T>> AddChoice(string label, T item)
        {
            return prompt.AddChoice(new SelectionPromptItem<T>(label, item));
        }

        public SelectionPrompt<SelectionPromptItem<T>> AddChoices(IEnumerable<T> items, Func<T, string> labelFunc)
        {
            foreach (var item in items)
            {
                prompt.AddChoice(new SelectionPromptItem<T>(labelFunc(item), item));
            }

            return prompt;
        }

        public SelectionPrompt<SelectionPromptItem<T>> AddChoices(params IEnumerable<(string, T)> items)
        {
            foreach (var (label, item) in items)
            {
                prompt.AddChoice(new SelectionPromptItem<T>(label, item));
            }

            return prompt;
        }

        public async Task<T> SelectItemAsync(IAnsiConsole console, CancellationToken ct = default)
        {
            SelectionPromptItem<T> result = await prompt.ShowAsync(console, ct);
            return result.Item;
        }
    }
}

public class SelectionPromptItem<T>(string label, T item)
{
    public string Label { get; } = label;
    public T Item { get; } = item;
}

# Umbra Standard Library Reference (`v1.0.0`)

The Umbra Virtual Machine (`VM`) registers a core suite of native Foreign Function Interface (**FFI**) functions in the global scope upon initialization. Every built-in function is an immutable instance of `UmbraNativeFunction` and can be invoked using either standard call syntax `func(arg)` or the pipeline operator `arg |> func`.

---

## Summary Table

| Function | Arity | Argument Types | Return Type | Description |
| :--- | :---: | :--- | :--- | :--- |
| [`len(val)`](#1-lenval) | 1 | `list` \| `string` | `number` | Returns the element count of a list or character length of a string. |
| [`clock()`](#2-clock) | 0 | — | `number` | Returns the current UTC timestamp in seconds (`double`). |
| [`push(list, item)`](#3-pushlist-item) | 2 | `list`, `any` | `list` | Appends `item` to the end of `list` and returns the modified list. |
| [`pop(list)`](#4-poplist) | 1 | `list` | `any` | Removes and returns the last element of `list`. |
| [`str(val)`](#5-strval) | 1 | `any` | `string` | Converts any Umbra value into its canonical string representation. |
| [`type(val)`](#6-typeval) | 1 | `any` | `string` | Returns the runtime type name of `val`. |

---

## Function Reference

### 1. `len(val)`
Returns the number of elements in a `list` or the number of UTF-16 characters in a `string`.

- **Signature**: `len(val) -> number`
- **Errors**: Raises a `RuntimeError` if `val` is neither a `list` nor a `string`.
- **Example**:
  ```umbra
  let items = [10, 20, 30, 40];
  print len(items);       // 4
  print items |> len;     // 4
  print "Umbra" |> len;   // 5
  ```

---

### 2. `clock()`
Returns the current system time in seconds (`double`) with millisecond precision, suitable for benchmarking and performance profiling.

- **Signature**: `clock() -> number`
- **Example**:
  ```umbra
  let start = clock();
  mut sum = 0;
  mut i = 0;
  while (i < 10000) {
      sum = sum + i;
      i = i + 1;
  }
  let elapsedMs = (clock() - start) * 1000;
  print "Elapsed: " + str(elapsedMs) + " ms";
  ```

---

### 3. `push(list, item)`
Appends `item` to the end of `list` in-place and returns the updated `list`.

- **Signature**: `push(list, item) -> list`
- **Errors**: Raises a `RuntimeError` if the first argument is not a `list`.
- **Example**:
  ```umbra
  mut stack = [1, 2];
  push(stack, 3);
  print stack; // [1, 2, 3]
  ```

---

### 4. `pop(list)`
Removes the last element from `list` in-place and returns the removed value.

- **Signature**: `pop(list) -> any`
- **Errors**:
  - Raises a `RuntimeError` if the argument is not a `list`.
  - Raises `RuntimeError: Cannot pop from an empty list.` if `list` is empty.
- **Example**:
  ```umbra
  mut stack = [10, 20, 30];
  let top = stack |> pop;
  print top;   // 30
  print stack; // [10, 20]
  ```

---

### 5. `str(val)`
Converts any Umbra runtime value into its canonical string representation (`"nil"`, `"true"`, `"false"`, formatted numbers, strings, `[...]` lists, `<fn name>`, or `<native fn name>`).

- **Signature**: `str(val) -> string`
- **Example**:
  ```umbra
  let values = [1, 2, 3];
  print "Values: " + (values |> str); // Values: [1, 2, 3]
  ```

---

### 6. `type(val)`
Returns a string identifying the runtime type of `val`.

- **Signature**: `type(val) -> string`
- **Possible Return Values**:
  - `"nil"`
  - `"bool"`
  - `"number"`
  - `"string"`
  - `"list"`
  - `"function"`
  - `"native"`
- **Example**:
  ```umbra
  print type(nil);        // nil
  print type(true);       // bool
  print type(42);         // number
  print type("Umbra");    // string
  print [1, 2] |> type;   // list
  print len |> type;      // native
  ```

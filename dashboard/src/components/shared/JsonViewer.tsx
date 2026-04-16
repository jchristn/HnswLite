interface JsonViewerProps {
  data: unknown;
  maxHeight?: number;
}

export default function JsonViewer(props: JsonViewerProps) {
  let text: string;
  if (typeof props.data === 'string') {
    try {
      text = JSON.stringify(JSON.parse(props.data), null, 2);
    } catch {
      text = props.data;
    }
  } else {
    try {
      text = JSON.stringify(props.data, null, 2);
    } catch {
      text = String(props.data);
    }
  }
  return (
    <pre
      className="code-block"
      style={props.maxHeight !== undefined ? { maxHeight: props.maxHeight } : undefined}
    >
      {text}
    </pre>
  );
}
